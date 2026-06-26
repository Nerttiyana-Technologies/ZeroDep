using System;

namespace ZeroDep.Fonts;

/// <summary>
/// A TrueType bytecode (hinting) interpreter. Runs the <c>fpgm</c>/<c>prep</c> programs once for a given ppem
/// and a glyph's instructions per call, grid-fitting the points in 26.6 fixed-point device space. Implements
/// the OpenType instruction set with FreeType-compatible fixed-point arithmetic. Internal; opt-in via
/// <see cref="TrueTypeFont.GetHintedGlyph"/>. Faults are surfaced as failure so the caller can fall back.
/// </summary>
internal sealed class TrueTypeHinter
{
    private const int MaxCallDepth = 128;
    private const long MaxInstructions = 4_000_000;

    private readonly byte[] _fpgm;
    private readonly byte[] _prep;
    private readonly int[] _cvt;          // scaled to 26.6
    private readonly int[] _storage;
    private readonly Function[] _functions;
    private readonly int _scale;          // 16.16: font units -> 26.6
    private readonly int _ppem;
    private readonly Zone _twilight;

    private GraphicsState _gs;
    private GraphicsState _default;
    private int[] _stack;
    private int _sp;
    private long _count;
    private Zone _zp0 = null!;
    private Zone _zp1 = null!;
    private Zone _zp2 = null!;
    private Zone _glyph;

    public TrueTypeHinter(byte[] cvt, byte[] fpgm, byte[] prep, int unitsPerEm, int ppem, TrueTypeFont.HintLimits limits)
    {
        _fpgm = fpgm ?? Array.Empty<byte>();
        _prep = prep ?? Array.Empty<byte>();
        _ppem = ppem;
        _scale = DivFix(ppem * 64, Math.Max(1, unitsPerEm));
        _storage = new int[Math.Max(1, limits.MaxStorage)];
        _functions = new Function[Math.Max(1, limits.MaxFunctionDefs) + limits.MaxInstructionDefs + 256];
        _stack = new int[Math.Max(64, limits.MaxStack) + 64];
        _twilight = new Zone(Math.Max(1, limits.MaxTwilightPoints));

        // Scale CVT (FWords -> 26.6).
        int nCvt = (cvt?.Length ?? 0) / 2;
        _cvt = new int[Math.Max(1, nCvt)];
        for (int i = 0; i < nCvt; i++)
        {
            short fw = (short)((cvt![i * 2] << 8) | cvt[(i * 2) + 1]);
            _cvt[i] = MulFix(fw, _scale);
        }

        _gs = GraphicsState.Default();
        ResetVectorsDerived();

        // fpgm defines functions; prep sets up CVT/storage/GS for this size.
        _glyph = _twilight;
        _zp0 = _zp1 = _zp2 = _twilight;
        if (_fpgm.Length > 0)
        {
            SafeRun(_fpgm, 0, _fpgm.Length);
        }

        _gs = GraphicsState.Default();
        ResetVectorsDerived();
        if (_prep.Length > 0)
        {
            _sp = 0;
            SafeRun(_prep, 0, _prep.Length);
        }

        _default = _gs;
    }

    public bool TryHintSimpleGlyph(TrueTypeFont.RawGlyph raw, int advanceWidth, int leftSideBearing, out int[] hintedX, out int[] hintedY)
    {
        int n = raw.X.Length;
        int total = n + 4; // 4 phantom points
        var zone = new Zone(total)
        {
            ContourEnds = raw.ContourEnds,
            NumContours = raw.ContourEnds.Length,
        };

        int minX = int.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (raw.X[i] < minX)
            {
                minX = raw.X[i];
            }
        }

        if (n == 0)
        {
            minX = 0;
        }

        int pp1x = minX - leftSideBearing;
        int pp2x = pp1x + advanceWidth;

        for (int i = 0; i < n; i++)
        {
            zone.OrgX[i] = MulFix(raw.X[i], _scale);
            zone.OrgY[i] = MulFix(raw.Y[i], _scale);
            zone.OnCurve[i] = raw.OnCurve[i];
        }

        zone.OrgX[n] = MulFix(pp1x, _scale);
        zone.OrgY[n] = 0;
        zone.OrgX[n + 1] = MulFix(pp2x, _scale);
        zone.OrgY[n + 1] = 0;
        zone.OrgX[n + 2] = 0;
        zone.OrgY[n + 2] = 0;
        zone.OrgX[n + 3] = 0;
        zone.OrgY[n + 3] = 0;

        for (int i = 0; i < total; i++)
        {
            zone.CurX[i] = zone.OrgX[i];
            zone.CurY[i] = zone.OrgY[i];
            zone.Touch[i] = 0;
        }

        zone.PhantomStart = n;

        // Restore the per-size default graphics state.
        _gs = _default;
        ResetVectorsDerived();
        _glyph = zone;
        _zp0 = _zp1 = _zp2 = zone;
        _sp = 0;
        _count = 0;

        bool ok = true;
        if (raw.Instructions.Length > 0 && (_gs.InstructControl & 1) == 0)
        {
            ok = SafeRun(raw.Instructions, 0, raw.Instructions.Length);
        }

        hintedX = new int[n];
        hintedY = new int[n];
        for (int i = 0; i < n; i++)
        {
            hintedX[i] = zone.CurX[i];
            hintedY[i] = zone.CurY[i];
        }

        return ok;
    }

    // ---- execution -------------------------------------------------------

    private bool SafeRun(byte[] code, int ip, int end)
    {
        try
        {
            Execute(code, ip, end, 0);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Execute(byte[] code, int ip, int end, int depth)
    {
        if (depth > MaxCallDepth)
        {
            return;
        }

        while (ip < end)
        {
            if (++_count > MaxInstructions)
            {
                throw new InvalidOperationException("instruction budget exceeded");
            }

            byte op = code[ip++];
            switch (op)
            {
                case 0x00:
                case 0x01: // SVTCA
                    SetVectorToAxis(op == 0x01, true, true);
                    break;
                case 0x02:
                case 0x03: // SPVTCA
                    SetVectorToAxis(op == 0x03, true, false);
                    break;
                case 0x04:
                case 0x05: // SFVTCA
                    SetVectorToAxis(op == 0x05, false, true);
                    break;
                case 0x06:
                case 0x07: // SPVTL
                    SetVectorToLine(op == 0x07, true);
                    break;
                case 0x08:
                case 0x09: // SFVTL
                    SetVectorToLine(op == 0x09, false);
                    break;
                case 0x0A: // SPVFS
                {
                    int y = Pop();
                    int x = Pop();
                    _gs.ProjX = (short)x;
                    _gs.ProjY = (short)y;
                    NormalizeProj();
                    _gs.DualX = _gs.ProjX;
                    _gs.DualY = _gs.ProjY;
                    ComputeFdotP();
                    break;
                }

                case 0x0B: // SFVFS
                {
                    int y = Pop();
                    int x = Pop();
                    _gs.FreeX = (short)x;
                    _gs.FreeY = (short)y;
                    NormalizeFree();
                    ComputeFdotP();
                    break;
                }

                case 0x0C: // GPV
                    Push(_gs.ProjX);
                    Push(_gs.ProjY);
                    break;
                case 0x0D: // GFV
                    Push(_gs.FreeX);
                    Push(_gs.FreeY);
                    break;
                case 0x0E: // SFVTPV
                    _gs.FreeX = _gs.ProjX;
                    _gs.FreeY = _gs.ProjY;
                    ComputeFdotP();
                    break;
                case 0x0F: // ISECT
                    Isect();
                    break;
                case 0x10: // SRP0
                    _gs.Rp0 = Pop();
                    break;
                case 0x11: // SRP1
                    _gs.Rp1 = Pop();
                    break;
                case 0x12: // SRP2
                    _gs.Rp2 = Pop();
                    break;
                case 0x13: // SZP0
                    _gs.Zp0 = Pop();
                    _zp0 = ZoneOf(_gs.Zp0);
                    break;
                case 0x14: // SZP1
                    _gs.Zp1 = Pop();
                    _zp1 = ZoneOf(_gs.Zp1);
                    break;
                case 0x15: // SZP2
                    _gs.Zp2 = Pop();
                    _zp2 = ZoneOf(_gs.Zp2);
                    break;
                case 0x16: // SZPS
                {
                    int z = Pop();
                    _gs.Zp0 = _gs.Zp1 = _gs.Zp2 = z;
                    _zp0 = _zp1 = _zp2 = ZoneOf(z);
                    break;
                }

                case 0x17: // SLOOP
                    _gs.Loop = Pop();
                    break;
                case 0x18: // RTG
                    _gs.RoundMode = RoundMode.Grid;
                    break;
                case 0x19: // RTHG
                    _gs.RoundMode = RoundMode.HalfGrid;
                    break;
                case 0x1A: // SMD
                    _gs.MinDistance = Pop();
                    break;
                case 0x1B: // ELSE
                    ip = SkipToEif(code, ip, end);
                    break;
                case 0x1C: // JMPR
                {
                    int offset = Pop();
                    ip = JumpTarget(ip, offset);
                    break;
                }

                case 0x1D: // SCVTCI
                    _gs.ControlValueCutIn = Pop();
                    break;
                case 0x1E: // SSWCI
                    _gs.SingleWidthCutIn = Pop();
                    break;
                case 0x1F: // SSW
                    _gs.SingleWidthValue = MulFix(Pop(), _scale);
                    break;
                case 0x20: // DUP
                {
                    int v = Peek();
                    Push(v);
                    break;
                }

                case 0x21: // POP
                    Pop();
                    break;
                case 0x22: // CLEAR
                    _sp = 0;
                    break;
                case 0x23: // SWAP
                {
                    int a = Pop();
                    int b = Pop();
                    Push(a);
                    Push(b);
                    break;
                }

                case 0x24: // DEPTH
                    Push(_sp);
                    break;
                case 0x25: // CINDEX
                {
                    int idx = Pop();
                    Push(idx >= 1 && idx <= _sp ? _stack[_sp - idx] : 0);
                    break;
                }

                case 0x26: // MINDEX
                {
                    int idx = Pop();
                    if (idx >= 1 && idx <= _sp)
                    {
                        int v = _stack[_sp - idx];
                        for (int k = _sp - idx; k < _sp - 1; k++)
                        {
                            _stack[k] = _stack[k + 1];
                        }

                        _sp--;
                        Push(v);
                    }

                    break;
                }

                case 0x27: // ALIGNPTS
                {
                    int p2 = Pop();
                    int p1 = Pop();
                    AlignPts(p1, p2);
                    break;
                }

                case 0x29: // UTP
                    Utp(Pop());
                    break;
                case 0x2A: // LOOPCALL
                {
                    int f = Pop();
                    int times = Pop();
                    for (int t = 0; t < times && t < 100000; t++)
                    {
                        CallFunction(f, depth);
                    }

                    break;
                }

                case 0x2B: // CALL
                    CallFunction(Pop(), depth);
                    break;
                case 0x2C: // FDEF
                    ip = DefineFunction(code, ip, end);
                    break;
                case 0x2D: // ENDF
                    return;
                case 0x2E:
                case 0x2F: // MDAP
                    Mdap((op & 1) != 0);
                    break;
                case 0x30:
                case 0x31: // IUP
                    Iup((op & 1) != 0);
                    break;
                case 0x32:
                case 0x33: // SHP
                    Shp((op & 1) != 0);
                    break;
                case 0x34:
                case 0x35: // SHC
                    Shc((op & 1) != 0, Pop());
                    break;
                case 0x36:
                case 0x37: // SHZ
                    Shz((op & 1) != 0, Pop());
                    break;
                case 0x38: // SHPIX
                    Shpix(Pop());
                    break;
                case 0x39: // IP
                    Ip();
                    break;
                case 0x3A:
                case 0x3B: // MSIRP
                    Msirp((op & 1) != 0);
                    break;
                case 0x3C: // ALIGNRP
                    AlignRp();
                    break;
                case 0x3D: // RTDG
                    _gs.RoundMode = RoundMode.DoubleGrid;
                    break;
                case 0x3E:
                case 0x3F: // MIAP
                    Miap((op & 1) != 0);
                    break;
                case 0x40: // NPUSHB
                {
                    int cnt = code[ip++];
                    for (int k = 0; k < cnt; k++)
                    {
                        Push(code[ip++]);
                    }

                    break;
                }

                case 0x41: // NPUSHW
                {
                    int cnt = code[ip++];
                    for (int k = 0; k < cnt; k++)
                    {
                        int v = (short)((code[ip] << 8) | code[ip + 1]);
                        ip += 2;
                        Push(v);
                    }

                    break;
                }

                case 0x42: // WS
                {
                    int v = Pop();
                    int a = Pop();
                    if (a >= 0 && a < _storage.Length)
                    {
                        _storage[a] = v;
                    }

                    break;
                }

                case 0x43: // RS
                {
                    int a = Pop();
                    Push(a >= 0 && a < _storage.Length ? _storage[a] : 0);
                    break;
                }

                case 0x44: // WCVTP
                {
                    int v = Pop();
                    int a = Pop();
                    if (a >= 0 && a < _cvt.Length)
                    {
                        _cvt[a] = v;
                    }

                    break;
                }

                case 0x70: // WCVTF
                {
                    int v = Pop();
                    int a = Pop();
                    if (a >= 0 && a < _cvt.Length)
                    {
                        _cvt[a] = MulFix(v, _scale);
                    }

                    break;
                }

                case 0x45: // RCVT
                {
                    int a = Pop();
                    Push(a >= 0 && a < _cvt.Length ? _cvt[a] : 0);
                    break;
                }

                case 0x46:
                case 0x47: // GC
                {
                    int p = Pop();
                    Push((op & 1) != 0 ? DualProject(_zp2, p) : Project(_zp2, p));
                    break;
                }

                case 0x48: // SCFS
                {
                    int value = Pop();
                    int p = Pop();
                    int cur = Project(_zp2, p);
                    Move(_zp2, p, value - cur);
                    break;
                }

                case 0x49:
                case 0x4A: // MD
                {
                    int p2 = Pop();
                    int p1 = Pop();
                    int dist = (op & 1) != 0
                        ? DualProjectDelta(_zp0, p2, _zp1, p1)
                        : ProjectDelta(_zp0, p2, _zp1, p1);
                    Push(dist);
                    break;
                }

                case 0x4B: // MPPEM
                    Push(_ppem);
                    break;
                case 0x4C: // MPS
                    Push(_ppem);
                    break;
                case 0x4D: // FLIPON
                    _gs.AutoFlip = true;
                    break;
                case 0x4E: // FLIPOFF
                    _gs.AutoFlip = false;
                    break;
                case 0x4F: // DEBUG
                    Pop();
                    break;
                case 0x50: // LT
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a < b ? 1 : 0);
                    break;
                }

                case 0x51: // LTEQ
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a <= b ? 1 : 0);
                    break;
                }

                case 0x52: // GT
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a > b ? 1 : 0);
                    break;
                }

                case 0x53: // GTEQ
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a >= b ? 1 : 0);
                    break;
                }

                case 0x54: // EQ
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a == b ? 1 : 0);
                    break;
                }

                case 0x55: // NEQ
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a != b ? 1 : 0);
                    break;
                }

                case 0x56: // ODD
                    Push((Round(Pop(), 0) & 127) == 64 ? 1 : 0);
                    break;
                case 0x57: // EVEN
                    Push((Round(Pop(), 0) & 127) == 0 ? 1 : 0);
                    break;
                case 0x58: // IF
                {
                    int cond = Pop();
                    if (cond == 0)
                    {
                        ip = SkipToElseOrEif(code, ip, end);
                    }

                    break;
                }

                case 0x59: // EIF
                    break;
                case 0x5A: // AND
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a != 0 && b != 0 ? 1 : 0);
                    break;
                }

                case 0x5B: // OR
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a != 0 || b != 0 ? 1 : 0);
                    break;
                }

                case 0x5C: // NOT
                    Push(Pop() == 0 ? 1 : 0);
                    break;
                case 0x5D: // DELTAP1
                    Deltap(0);
                    break;
                case 0x71: // DELTAP2
                    Deltap(16);
                    break;
                case 0x72: // DELTAP3
                    Deltap(32);
                    break;
                case 0x73: // DELTAC1
                    Deltac(0);
                    break;
                case 0x74: // DELTAC2
                    Deltac(16);
                    break;
                case 0x75: // DELTAC3
                    Deltac(32);
                    break;
                case 0x5E: // SDB
                    _gs.DeltaBase = Pop();
                    break;
                case 0x5F: // SDS
                    _gs.DeltaShift = Pop();
                    break;
                case 0x60: // ADD
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a + b);
                    break;
                }

                case 0x61: // SUB
                {
                    int b = Pop();
                    int a = Pop();
                    Push(a - b);
                    break;
                }

                case 0x62: // DIV
                {
                    int b = Pop();
                    int a = Pop();
                    Push(b != 0 ? MulDiv(a, 64, b) : 0);
                    break;
                }

                case 0x63: // MUL
                {
                    int b = Pop();
                    int a = Pop();
                    Push(MulDiv(a, b, 64));
                    break;
                }

                case 0x64: // ABS
                    Push(Math.Abs(Pop()));
                    break;
                case 0x65: // NEG
                    Push(-Pop());
                    break;
                case 0x66: // FLOOR
                    Push(Pop() & ~63);
                    break;
                case 0x67: // CEILING
                    Push((Pop() + 63) & ~63);
                    break;
                case 0x68:
                case 0x69:
                case 0x6A:
                case 0x6B: // ROUND
                    Push(Round(Pop(), 0));
                    break;
                case 0x6C:
                case 0x6D:
                case 0x6E:
                case 0x6F: // NROUND
                    Push(Pop());
                    break;
                case 0x76: // SROUND
                    SetSuperRound(64, Pop());
                    _gs.RoundMode = RoundMode.Super;
                    break;
                case 0x77: // S45ROUND
                    SetSuperRound(46, Pop()); // ~64/sqrt2
                    _gs.RoundMode = RoundMode.Super;
                    break;
                case 0x78: // JROT
                {
                    int cond = Pop();
                    int offset = Pop();
                    if (cond != 0)
                    {
                        ip = JumpTarget(ip, offset);
                    }

                    break;
                }

                case 0x79: // JROF
                {
                    int cond = Pop();
                    int offset = Pop();
                    if (cond == 0)
                    {
                        ip = JumpTarget(ip, offset);
                    }

                    break;
                }

                case 0x7A: // ROFF
                    _gs.RoundMode = RoundMode.Off;
                    break;
                case 0x7C: // RUTG
                    _gs.RoundMode = RoundMode.UpToGrid;
                    break;
                case 0x7D: // RDTG
                    _gs.RoundMode = RoundMode.DownToGrid;
                    break;
                case 0x7E: // SANGW
                    Pop();
                    break;
                case 0x7F: // AA
                    Pop();
                    break;
                case 0x80: // FLIPPT
                    FlipPt();
                    break;
                case 0x81: // FLIPRGON
                    FlipRange(true);
                    break;
                case 0x82: // FLIPRGOFF
                    FlipRange(false);
                    break;
                case 0x85: // SCANCTRL
                    _gs.ScanControl = Pop();
                    break;
                case 0x86:
                case 0x87: // SDPVTL
                    SetDualVectorToLine(op == 0x87);
                    break;
                case 0x88: // GETINFO
                    GetInfo();
                    break;
                case 0x89: // IDEF
                    ip = DefineInstruction(code, ip, end);
                    break;
                case 0x8A: // ROLL
                    Roll();
                    break;
                case 0x8B: // MAX
                {
                    int b = Pop();
                    int a = Pop();
                    Push(Math.Max(a, b));
                    break;
                }

                case 0x8C: // MIN
                {
                    int b = Pop();
                    int a = Pop();
                    Push(Math.Min(a, b));
                    break;
                }

                case 0x8D: // SCANTYPE
                    _gs.ScanType = Pop();
                    break;
                case 0x8E: // INSTCTRL
                {
                    int selector = Pop();
                    int value = Pop();
                    if (selector >= 1 && selector <= 2)
                    {
                        int mask = selector == 1 ? 1 : 2;
                        if (value != 0)
                        {
                            _gs.InstructControl |= mask;
                        }
                        else
                        {
                            _gs.InstructControl &= ~mask;
                        }
                    }

                    break;
                }

                default:
                    if (op >= 0xB0 && op <= 0xB7) // PUSHB
                    {
                        int cnt = op - 0xB0 + 1;
                        for (int k = 0; k < cnt; k++)
                        {
                            Push(code[ip++]);
                        }
                    }
                    else if (op >= 0xB8 && op <= 0xBF) // PUSHW
                    {
                        int cnt = op - 0xB8 + 1;
                        for (int k = 0; k < cnt; k++)
                        {
                            int v = (short)((code[ip] << 8) | code[ip + 1]);
                            ip += 2;
                            Push(v);
                        }
                    }
                    else if (op >= 0xC0 && op <= 0xDF) // MDRP
                    {
                        Mdrp(op);
                    }
                    else if (op >= 0xE0) // MIRP
                    {
                        Mirp(op);
                    }

                    break;
            }
        }
    }

    // ---- control flow helpers -------------------------------------------

    private static int JumpTarget(int ipAfterOpcode, int offset)
    {
        // JMPR/JROT/JROF offsets are relative to the position of the opcode itself.
        return (ipAfterOpcode - 1) + offset;
    }

    private void CallFunction(int f, int depth)
    {
        if (f < 0 || f >= _functions.Length)
        {
            return;
        }

        Function fn = _functions[f];
        if (fn.Code is null)
        {
            return;
        }

        Execute(fn.Code, fn.Start, fn.End, depth + 1);
    }

    private int DefineFunction(byte[] code, int ip, int end)
    {
        int f = Pop();
        int start = ip;
        int scan = ip;
        while (scan < end)
        {
            byte op = code[scan];
            if (op == 0x2D) // ENDF
            {
                if (f >= 0 && f < _functions.Length)
                {
                    _functions[f] = new Function(code, start, scan);
                }

                return scan + 1;
            }

            scan = NextInstruction(code, scan, end);
        }

        return end;
    }

    private int DefineInstruction(byte[] code, int ip, int end)
    {
        Pop(); // opcode to define — body is skipped; custom opcodes are treated as no-ops.
        int scan = ip;
        while (scan < end)
        {
            if (code[scan] == 0x2D)
            {
                return scan + 1;
            }

            scan = NextInstruction(code, scan, end);
        }

        return end;
    }

    private static int SkipToElseOrEif(byte[] code, int ip, int end)
    {
        int nest = 0;
        while (ip < end)
        {
            byte op = code[ip];
            if (op == 0x58) // IF
            {
                nest++;
            }
            else if (op == 0x59) // EIF
            {
                if (nest == 0)
                {
                    return ip + 1;
                }

                nest--;
            }
            else if (op == 0x1B && nest == 0) // ELSE
            {
                return ip + 1;
            }

            ip = NextInstruction(code, ip, end);
        }

        return end;
    }

    private static int SkipToEif(byte[] code, int ip, int end)
    {
        int nest = 0;
        while (ip < end)
        {
            byte op = code[ip];
            if (op == 0x58)
            {
                nest++;
            }
            else if (op == 0x59)
            {
                if (nest == 0)
                {
                    return ip + 1;
                }

                nest--;
            }

            ip = NextInstruction(code, ip, end);
        }

        return end;
    }

    private static int NextInstruction(byte[] code, int ip, int end)
    {
        byte op = code[ip++];
        if (op == 0x40) // NPUSHB
        {
            int cnt = ip < end ? code[ip++] : 0;
            return ip + cnt;
        }

        if (op == 0x41) // NPUSHW
        {
            int cnt = ip < end ? code[ip++] : 0;
            return ip + (cnt * 2);
        }

        if (op >= 0xB0 && op <= 0xB7)
        {
            return ip + (op - 0xB0 + 1);
        }

        if (op >= 0xB8 && op <= 0xBF)
        {
            return ip + ((op - 0xB8 + 1) * 2);
        }

        return ip;
    }

    // ---- stack -----------------------------------------------------------

    private void Push(int v)
    {
        if (_sp >= _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_sp++] = v;
    }

    private int Pop() => _sp > 0 ? _stack[--_sp] : 0;

    private int Peek() => _sp > 0 ? _stack[_sp - 1] : 0;

    private void Roll()
    {
        if (_sp < 3)
        {
            return;
        }

        int a = _stack[_sp - 1];
        int b = _stack[_sp - 2];
        int c = _stack[_sp - 3];
        _stack[_sp - 3] = b;
        _stack[_sp - 2] = a;
        _stack[_sp - 1] = c;
    }

    // ---- vectors & projection -------------------------------------------

    private Zone ZoneOf(int z) => z == 0 ? _twilight : _glyph;

    private void SetVectorToAxis(bool xAxis, bool proj, bool free)
    {
        short vx = (short)(xAxis ? 0x4000 : 0);
        short vy = (short)(xAxis ? 0 : 0x4000);
        if (proj)
        {
            _gs.ProjX = vx;
            _gs.ProjY = vy;
            _gs.DualX = vx;
            _gs.DualY = vy;
        }

        if (free)
        {
            _gs.FreeX = vx;
            _gs.FreeY = vy;
        }

        ComputeFdotP();
    }

    private void SetVectorToLine(bool perpendicular, bool proj)
    {
        int p2 = Pop();
        int p1 = Pop();
        Zone z2 = _zp1;
        Zone z1 = _zp2;
        int dx = z1.CurX[p1] - z2.CurX[p2];
        int dy = z1.CurY[p1] - z2.CurY[p2];
        (short vx, short vy) = UnitVector(dx, dy, perpendicular);
        if (proj)
        {
            _gs.ProjX = vx;
            _gs.ProjY = vy;
            _gs.DualX = vx;
            _gs.DualY = vy;
        }
        else
        {
            _gs.FreeX = vx;
            _gs.FreeY = vy;
        }

        ComputeFdotP();
    }

    private void SetDualVectorToLine(bool perpendicular)
    {
        int p2 = Pop();
        int p1 = Pop();
        int dx = _zp2.OrgX[p1] - _zp1.OrgX[p2];
        int dy = _zp2.OrgY[p1] - _zp1.OrgY[p2];
        (short vx, short vy) = UnitVector(dx, dy, perpendicular);
        _gs.DualX = vx;
        _gs.DualY = vy;
        _gs.ProjX = vx;
        _gs.ProjY = vy;
        ComputeFdotP();
    }

    private static (short X, short Y) UnitVector(int dx, int dy, bool perpendicular)
    {
        if (perpendicular)
        {
            int t = dx;
            dx = -dy;
            dy = t;
        }

        double len = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
        if (len < 1e-9)
        {
            return (0x4000, 0);
        }

        int vx = (int)Math.Round(dx / len * 0x4000);
        int vy = (int)Math.Round(dy / len * 0x4000);
        return ((short)vx, (short)vy);
    }

    private void NormalizeProj()
    {
        (short vx, short vy) = UnitVector(_gs.ProjX, _gs.ProjY, false);
        _gs.ProjX = vx;
        _gs.ProjY = vy;
    }

    private void NormalizeFree()
    {
        (short vx, short vy) = UnitVector(_gs.FreeX, _gs.FreeY, false);
        _gs.FreeX = vx;
        _gs.FreeY = vy;
    }

    private void ResetVectorsDerived() => ComputeFdotP();

    private void ComputeFdotP()
    {
        long v = ((long)_gs.ProjX * _gs.FreeX) + ((long)_gs.ProjY * _gs.FreeY);
        int f = (int)(v >> 14);
        _gs.FdotP = f == 0 ? 0x4000 : f;
    }

    private int Project(Zone z, int p) => Dot(z.CurX[p], z.CurY[p], _gs.ProjX, _gs.ProjY);

    private int DualProject(Zone z, int p) => Dot(z.OrgX[p], z.OrgY[p], _gs.DualX, _gs.DualY);

    private int ProjectDelta(Zone za, int pa, Zone zb, int pb)
        => Dot(za.CurX[pa] - zb.CurX[pb], za.CurY[pa] - zb.CurY[pb], _gs.ProjX, _gs.ProjY);

    private int DualProjectDelta(Zone za, int pa, Zone zb, int pb)
        => Dot(za.OrgX[pa] - zb.OrgX[pb], za.OrgY[pa] - zb.OrgY[pb], _gs.DualX, _gs.DualY);

    private static int Dot(int dx, int dy, int vx, int vy)
    {
        long s = ((long)dx * vx) + ((long)dy * vy);
        return (int)((s + 0x2000) >> 14);
    }

    private void Move(Zone z, int p, int distance)
    {
        if (_gs.FdotP == 0)
        {
            return;
        }

        if (_gs.FreeX != 0)
        {
            z.CurX[p] += MulDiv(distance, _gs.FreeX, _gs.FdotP);
            z.Touch[p] |= 1;
        }

        if (_gs.FreeY != 0)
        {
            z.CurY[p] += MulDiv(distance, _gs.FreeY, _gs.FdotP);
            z.Touch[p] |= 2;
        }
    }

    // ---- point instructions ---------------------------------------------

    private void Mdap(bool round)
    {
        int p = Pop();
        int cur = Project(_zp0, p);
        int distance = round ? Round(cur, 0) - cur : 0;
        Move(_zp0, p, distance);
        _gs.Rp0 = p;
        _gs.Rp1 = p;
    }

    private void Miap(bool round)
    {
        int cvtIndex = Pop();
        int p = Pop();
        int distance = cvtIndex >= 0 && cvtIndex < _cvt.Length ? _cvt[cvtIndex] : 0;
        if (_gs.Zp0 == 0)
        {
            _zp0.OrgX[p] = MulFix14(distance, _gs.FreeX);
            _zp0.OrgY[p] = MulFix14(distance, _gs.FreeY);
            _zp0.CurX[p] = _zp0.OrgX[p];
            _zp0.CurY[p] = _zp0.OrgY[p];
        }

        int orgDist = Project(_zp0, p);
        if (round)
        {
            if (Math.Abs(distance - orgDist) > _gs.ControlValueCutIn)
            {
                distance = orgDist;
            }

            distance = Round(distance, 0);
        }

        Move(_zp0, p, distance - orgDist);
        _gs.Rp0 = p;
        _gs.Rp1 = p;
    }

    private void Mdrp(byte op)
    {
        bool setRp0 = (op & 0x10) != 0;
        bool keepMin = (op & 0x08) != 0;
        bool round = (op & 0x04) != 0;
        int p = Pop();

        int orgDist = DualProjectDelta(_zp1, p, _zp0, _gs.Rp0);
        orgDist = ApplySingleWidth(orgDist);

        int distance = round ? Round(orgDist, 0) : orgDist;
        if (keepMin)
        {
            distance = KeepMinDistance(distance, orgDist);
        }

        int curDist = ProjectDelta(_zp1, p, _zp0, _gs.Rp0);
        Move(_zp1, p, distance - curDist);

        _gs.Rp1 = _gs.Rp0;
        _gs.Rp2 = p;
        if (setRp0)
        {
            _gs.Rp0 = p;
        }
    }

    private void Mirp(byte op)
    {
        bool setRp0 = (op & 0x10) != 0;
        bool keepMin = (op & 0x08) != 0;
        bool round = (op & 0x04) != 0;
        int cvtIndex = Pop();
        int p = Pop();

        int cvtDist = cvtIndex >= 0 && cvtIndex < _cvt.Length ? _cvt[cvtIndex] : 0;
        cvtDist = ApplySingleWidth(cvtDist);

        if (_gs.Zp1 == 0)
        {
            _zp1.OrgX[p] = _zp0.OrgX[_gs.Rp0] + MulFix14(cvtDist, _gs.FreeX);
            _zp1.OrgY[p] = _zp0.OrgY[_gs.Rp0] + MulFix14(cvtDist, _gs.FreeY);
            _zp1.CurX[p] = _zp1.OrgX[p];
            _zp1.CurY[p] = _zp1.OrgY[p];
        }

        int orgDist = DualProjectDelta(_zp1, p, _zp0, _gs.Rp0);
        int curDist = ProjectDelta(_zp1, p, _zp0, _gs.Rp0);

        if (_gs.AutoFlip && ((orgDist ^ cvtDist) < 0))
        {
            cvtDist = -cvtDist;
        }

        int distance;
        if (round)
        {
            if (_gs.Zp0 == _gs.Zp1 && Math.Abs(cvtDist - orgDist) > _gs.ControlValueCutIn)
            {
                cvtDist = orgDist;
            }

            distance = Round(cvtDist, 0);
        }
        else
        {
            distance = cvtDist;
        }

        if (keepMin)
        {
            distance = KeepMinDistance(distance, orgDist);
        }

        Move(_zp1, p, distance - curDist);

        _gs.Rp1 = _gs.Rp0;
        _gs.Rp2 = p;
        if (setRp0)
        {
            _gs.Rp0 = p;
        }
    }

    private int ApplySingleWidth(int distance)
    {
        if (_gs.SingleWidthCutIn > 0)
        {
            if (Math.Abs(distance - _gs.SingleWidthValue) < _gs.SingleWidthCutIn)
            {
                distance = distance >= 0 ? _gs.SingleWidthValue : -_gs.SingleWidthValue;
            }
        }

        return distance;
    }

    private int KeepMinDistance(int distance, int orgDist)
    {
        if (orgDist >= 0)
        {
            return distance < _gs.MinDistance ? _gs.MinDistance : distance;
        }

        return distance > -_gs.MinDistance ? -_gs.MinDistance : distance;
    }

    private void Msirp(bool setRp0)
    {
        int distance = Pop();
        int p = Pop();
        if (_gs.Zp1 == 0)
        {
            _zp1.OrgX[p] = _zp0.OrgX[_gs.Rp0] + MulFix14(distance, _gs.FreeX);
            _zp1.OrgY[p] = _zp0.OrgY[_gs.Rp0] + MulFix14(distance, _gs.FreeY);
            _zp1.CurX[p] = _zp1.OrgX[p];
            _zp1.CurY[p] = _zp1.OrgY[p];
        }

        int curDist = ProjectDelta(_zp1, p, _zp0, _gs.Rp0);
        Move(_zp1, p, distance - curDist);
        _gs.Rp1 = _gs.Rp0;
        _gs.Rp2 = p;
        if (setRp0)
        {
            _gs.Rp0 = p;
        }
    }

    private void AlignRp()
    {
        int loop = _gs.Loop;
        for (int i = 0; i < loop; i++)
        {
            int p = Pop();
            int distance = -ProjectDelta(_zp1, p, _zp0, _gs.Rp0);
            Move(_zp1, p, distance);
        }

        _gs.Loop = 1;
    }

    private void AlignPts(int p1, int p2)
    {
        int distance = ProjectDelta(_zp1, p2, _zp0, p1) / 2;
        Move(_zp1, p2, -distance);
        Move(_zp0, p1, distance);
    }

    private void Shpix(int amount)
    {
        int dx = MulFix14(amount, _gs.FreeX);
        int dy = MulFix14(amount, _gs.FreeY);
        int loop = _gs.Loop;
        for (int i = 0; i < loop; i++)
        {
            int p = Pop();
            _zp2.CurX[p] += dx;
            _zp2.CurY[p] += dy;
            _zp2.Touch[p] |= 3;
        }

        _gs.Loop = 1;
    }

    private void Shp(bool useRp1)
    {
        (int dx, int dy) = ComputeDisplacement(useRp1);
        int loop = _gs.Loop;
        for (int i = 0; i < loop; i++)
        {
            int p = Pop();
            _zp2.CurX[p] += dx;
            _zp2.CurY[p] += dy;
            _zp2.Touch[p] |= 3;
        }

        _gs.Loop = 1;
    }

    private void Shc(bool useRp1, int contour)
    {
        (int dx, int dy) = ComputeDisplacement(useRp1);
        Zone z = _zp2;
        if (contour < 0 || contour >= z.NumContours)
        {
            return;
        }

        int start = contour == 0 ? 0 : z.ContourEnds[contour - 1] + 1;
        int end = z.ContourEnds[contour];
        for (int p = start; p <= end && p < z.PhantomStart; p++)
        {
            z.CurX[p] += dx;
            z.CurY[p] += dy;
            z.Touch[p] |= 3;
        }
    }

    private void Shz(bool useRp1, int zoneArg)
    {
        (int dx, int dy) = ComputeDisplacement(useRp1);
        Zone z = ZoneOf(zoneArg);
        for (int p = 0; p < z.PhantomStart; p++)
        {
            z.CurX[p] += dx;
            z.CurY[p] += dy;
        }
    }

    private (int Dx, int Dy) ComputeDisplacement(bool useRp1)
    {
        Zone z = useRp1 ? _zp0 : _zp1;
        int rp = useRp1 ? _gs.Rp1 : _gs.Rp2;
        int distance = Project(z, rp) - DualProject(z, rp);
        int dx = MulDiv(distance, _gs.FreeX, _gs.FdotP);
        int dy = MulDiv(distance, _gs.FreeY, _gs.FdotP);
        return (dx, dy);
    }

    private void Ip()
    {
        int rp1 = _gs.Rp1;
        int rp2 = _gs.Rp2;
        int orgA = DualProject(_zp0, rp1);
        int orgB = DualProject(_zp1, rp2);
        int curA = Project(_zp0, rp1);
        int curB = Project(_zp1, rp2);
        int range = orgB - orgA;
        int curRange = curB - curA;

        int loop = _gs.Loop;
        for (int i = 0; i < loop; i++)
        {
            int p = Pop();
            int orgP = DualProject(_zp2, p);
            int curP = Project(_zp2, p);
            int newP = range != 0
                ? curA + MulDiv(orgP - orgA, curRange, range)
                : curA + (orgP - orgA);
            Move(_zp2, p, newP - curP);
        }

        _gs.Loop = 1;
    }

    private void Iup(bool xAxis)
    {
        Zone z = _glyph;
        int n = z.PhantomStart;
        if (n <= 0)
        {
            return;
        }

        int touchMask = xAxis ? 1 : 2;
        int start = 0;
        for (int c = 0; c < z.NumContours; c++)
        {
            int end = z.ContourEnds[c];
            if (end >= n)
            {
                end = n - 1;
            }

            IupContour(z, start, end, xAxis, touchMask);
            start = end + 1;
        }
    }

    private static void IupContour(Zone z, int start, int end, bool xAxis, int touchMask)
    {
        int len = end - start + 1;
        if (len <= 0)
        {
            return;
        }

        int[] cur = xAxis ? z.CurX : z.CurY;
        int[] org = xAxis ? z.OrgX : z.OrgY;

        // Find first touched point.
        int first = -1;
        for (int i = start; i <= end; i++)
        {
            if ((z.Touch[i] & touchMask) != 0)
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return; // no touched points → contour unchanged
        }

        int prev = first;
        int p = NextIn(first, start, end);
        while (true)
        {
            if ((z.Touch[p] & touchMask) != 0)
            {
                InterpolateRun(cur, org, z, start, end, prev, p, touchMask);
                prev = p;
            }

            if (p == first)
            {
                break;
            }

            p = NextIn(p, start, end);
        }
    }

    private static void InterpolateRun(int[] cur, int[] org, Zone z, int start, int end, int p1, int p2, int touchMask)
    {
        // Interpolate untouched points strictly between p1 and p2 (wrapping within the contour).
        int i = NextIn(p1, start, end);
        if (i == p2)
        {
            return;
        }

        int org1 = org[p1];
        int org2 = org[p2];
        int cur1 = cur[p1];
        int cur2 = cur[p2];
        int loMin;
        int loMax;
        if (org1 <= org2)
        {
            loMin = org1;
            loMax = org2;
        }
        else
        {
            loMin = org2;
            loMax = org1;
        }

        while (i != p2)
        {
            if ((z.Touch[i] & touchMask) == 0)
            {
                int oi = org[i];
                int v;
                if (oi <= loMin)
                {
                    v = (org1 <= org2 ? cur1 : cur2) + (oi - loMin);
                }
                else if (oi >= loMax)
                {
                    v = (org1 <= org2 ? cur2 : cur1) + (oi - loMax);
                }
                else if (org2 != org1)
                {
                    v = cur1 + MulDiv(oi - org1, cur2 - cur1, org2 - org1);
                }
                else
                {
                    v = cur1;
                }

                cur[i] = v;
            }

            i = NextIn(i, start, end);
        }
    }

    private static int NextIn(int i, int start, int end) => i >= end ? start : i + 1;

    private void Utp(int p)
    {
        int mask = 3;
        if (_gs.FreeX != 0 && _gs.FreeY == 0)
        {
            mask = ~1 & 3;
        }
        else if (_gs.FreeY != 0 && _gs.FreeX == 0)
        {
            mask = ~2 & 3;
        }
        else
        {
            mask = 0;
        }

        if (p >= 0 && p < _zp0.Touch.Length)
        {
            _zp0.Touch[p] &= (byte)mask;
        }
    }

    private void FlipPt()
    {
        int loop = _gs.Loop;
        Zone z = _zp0;
        for (int i = 0; i < loop; i++)
        {
            int p = Pop();
            if (p >= 0 && p < z.PhantomStart)
            {
                z.OnCurve[p] = !z.OnCurve[p];
            }
        }

        _gs.Loop = 1;
    }

    private void FlipRange(bool on)
    {
        int high = Pop();
        int low = Pop();
        Zone z = _zp0;
        for (int p = low; p <= high && p < z.PhantomStart; p++)
        {
            if (p >= 0)
            {
                z.OnCurve[p] = on;
            }
        }
    }

    private void Isect()
    {
        int b1 = Pop();
        int b0 = Pop();
        int a1 = Pop();
        int a0 = Pop();
        int p = Pop();
        Zone zp = _zp2;
        Zone za = _zp1;
        Zone zb = _zp0;

        double a0x = za.CurX[a0], a0y = za.CurY[a0];
        double a1x = za.CurX[a1], a1y = za.CurY[a1];
        double b0x = zb.CurX[b0], b0y = zb.CurY[b0];
        double b1x = zb.CurX[b1], b1y = zb.CurY[b1];

        double dax = a1x - a0x, day = a1y - a0y;
        double dbx = b1x - b0x, dby = b1y - b0y;
        double denom = (dax * dby) - (day * dbx);
        if (Math.Abs(denom) < 1e-9)
        {
            zp.CurX[p] = (int)((a0x + a1x + b0x + b1x) / 4);
            zp.CurY[p] = (int)((a0y + a1y + b0y + b1y) / 4);
        }
        else
        {
            double t = (((b0x - a0x) * dby) - ((b0y - a0y) * dbx)) / denom;
            zp.CurX[p] = (int)Math.Round(a0x + (t * dax));
            zp.CurY[p] = (int)Math.Round(a0y + (t * day));
        }

        zp.Touch[p] |= 3;
    }

    private void Deltap(int baseOffset)
    {
        int count = Pop();
        for (int i = 0; i < count; i++)
        {
            int p = Pop();
            int arg = Pop();
            ApplyDelta(p, arg, baseOffset, false, 0);
        }
    }

    private void Deltac(int baseOffset)
    {
        int count = Pop();
        for (int i = 0; i < count; i++)
        {
            int c = Pop();
            int arg = Pop();
            ApplyDelta(0, arg, baseOffset, true, c);
        }
    }

    private void ApplyDelta(int p, int arg, int baseOffset, bool cvt, int cvtIndex)
    {
        int relPpem = ((arg >> 4) & 0xF) + _gs.DeltaBase + baseOffset;
        if (relPpem != _ppem)
        {
            return;
        }

        int stepIndex = arg & 0xF;
        int mag = stepIndex < 8 ? stepIndex - 8 : stepIndex - 7;
        int step = 1 << (6 - _gs.DeltaShift);
        int amount = mag * step;
        if (cvt)
        {
            if (cvtIndex >= 0 && cvtIndex < _cvt.Length)
            {
                _cvt[cvtIndex] += amount;
            }
        }
        else if (p >= 0 && p < _zp0.PhantomStart)
        {
            Move(_zp0, p, amount);
        }
    }

    private void GetInfo()
    {
        int selector = Pop();
        int result = 0;
        if ((selector & 0x01) != 0)
        {
            result |= 40; // version (Windows-compatible rasterizer)
        }

        if ((selector & 0x20) != 0)
        {
            result |= 1 << 12; // grayscale rasterizer
        }

        Push(result);
    }

    // ---- rounding --------------------------------------------------------

    private int Round(int distance, int compensation)
    {
        switch (_gs.RoundMode)
        {
            case RoundMode.Off:
                return RoundNone(distance, compensation);
            case RoundMode.Grid:
                return RoundToGrid(distance, compensation);
            case RoundMode.HalfGrid:
                return RoundToHalfGrid(distance, compensation);
            case RoundMode.DoubleGrid:
                return RoundToDoubleGrid(distance, compensation);
            case RoundMode.DownToGrid:
                return RoundDownToGrid(distance, compensation);
            case RoundMode.UpToGrid:
                return RoundUpToGrid(distance, compensation);
            default:
                return RoundSuper(distance, compensation);
        }
    }

    private static int RoundNone(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = d + c;
            if (val < 0)
            {
                val = 0;
            }
        }
        else
        {
            val = d - c;
            if (val > 0)
            {
                val = 0;
            }
        }

        return val;
    }

    private static int RoundToGrid(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = (d + c + 32) & ~63;
            if (val < 0)
            {
                val = 0;
            }
        }
        else
        {
            val = -((c - d + 32) & ~63);
            if (val > 0)
            {
                val = 0;
            }
        }

        return val;
    }

    private static int RoundToHalfGrid(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = ((d + c) & ~63) + 32;
            if (val < 0)
            {
                val = 32;
            }
        }
        else
        {
            val = -(((c - d) & ~63) + 32);
            if (val > 0)
            {
                val = -32;
            }
        }

        return val;
    }

    private static int RoundToDoubleGrid(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = (d + c + 16) & ~31;
            if (val < 0)
            {
                val = 0;
            }
        }
        else
        {
            val = -((c - d + 16) & ~31);
            if (val > 0)
            {
                val = 0;
            }
        }

        return val;
    }

    private static int RoundDownToGrid(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = (d + c) & ~63;
            if (val < 0)
            {
                val = 0;
            }
        }
        else
        {
            val = -((c - d) & ~63);
            if (val > 0)
            {
                val = 0;
            }
        }

        return val;
    }

    private static int RoundUpToGrid(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = (d + c + 63) & ~63;
            if (val < 0)
            {
                val = 0;
            }
        }
        else
        {
            val = -((c - d + 63) & ~63);
            if (val > 0)
            {
                val = 0;
            }
        }

        return val;
    }

    private int RoundSuper(int d, int c)
    {
        int val;
        if (d >= 0)
        {
            val = (d - _gs.Phase + _gs.Threshold + c) & -_gs.Period;
            val += _gs.Phase;
            if (val < 0)
            {
                val = _gs.Phase;
            }
        }
        else
        {
            val = -((_gs.Threshold - _gs.Phase - d + c) & -_gs.Period);
            val -= _gs.Phase;
            if (val > 0)
            {
                val = -_gs.Phase;
            }
        }

        return val;
    }

    private void SetSuperRound(int gridPeriod, int selector)
    {
        switch (selector & 0xC0)
        {
            case 0x00:
                _gs.Period = gridPeriod / 2;
                break;
            case 0x40:
                _gs.Period = gridPeriod;
                break;
            case 0x80:
                _gs.Period = gridPeriod * 2;
                break;
            default:
                _gs.Period = gridPeriod;
                break;
        }

        if (_gs.Period == 0)
        {
            _gs.Period = 1;
        }

        switch (selector & 0x30)
        {
            case 0x00:
                _gs.Phase = 0;
                break;
            case 0x10:
                _gs.Phase = _gs.Period / 4;
                break;
            case 0x20:
                _gs.Phase = _gs.Period / 2;
                break;
            default:
                _gs.Phase = _gs.Period * 3 / 4;
                break;
        }

        if ((selector & 0x0F) == 0)
        {
            _gs.Threshold = _gs.Period - 1;
        }
        else
        {
            _gs.Threshold = ((selector & 0x0F) - 4) * _gs.Period / 8;
        }
    }

    // ---- fixed-point primitives -----------------------------------------

    private static int MulFix(int a, int b)
    {
        long ab = (long)a * b;
        long sign = ab >= 0 ? 1 : -1;
        ab *= sign;
        long r = (ab + 0x8000) >> 16;
        return (int)(sign * r);
    }

    private static int MulFix14(int a, int b)
    {
        long ab = (long)a * b;
        long sign = ab >= 0 ? 1 : -1;
        ab *= sign;
        long r = (ab + 0x2000) >> 14;
        return (int)(sign * r);
    }

    private static int MulDiv(int a, int b, int c)
    {
        long ab = (long)a * b;
        long sign = 1;
        if (ab < 0)
        {
            ab = -ab;
            sign = -1;
        }

        long cc = c;
        if (cc < 0)
        {
            cc = -cc;
            sign = -sign;
        }

        if (cc == 0)
        {
            return 0;
        }

        long r = (ab + (cc >> 1)) / cc;
        return (int)(sign * r);
    }

    private static int DivFix(int a, int b)
    {
        long aa = a;
        long bb = b;
        long sign = 1;
        if (aa < 0)
        {
            aa = -aa;
            sign = -1;
        }

        if (bb < 0)
        {
            bb = -bb;
            sign = -sign;
        }

        if (bb == 0)
        {
            return 0;
        }

        long r = ((aa << 16) + (bb >> 1)) / bb;
        return (int)(sign * r);
    }

    // ---- supporting types -----------------------------------------------

    private readonly struct Function
    {
        public Function(byte[] code, int start, int end)
        {
            Code = code;
            Start = start;
            End = end;
        }

        public byte[]? Code { get; }

        public int Start { get; }

        public int End { get; }
    }

    private enum RoundMode
    {
        Grid,
        HalfGrid,
        DoubleGrid,
        DownToGrid,
        UpToGrid,
        Off,
        Super,
    }

    private sealed class Zone
    {
        public Zone(int capacity)
        {
            int n = Math.Max(1, capacity);
            CurX = new int[n];
            CurY = new int[n];
            OrgX = new int[n];
            OrgY = new int[n];
            OnCurve = new bool[n];
            Touch = new byte[n];
            ContourEnds = Array.Empty<int>();
        }

        public int[] CurX { get; }

        public int[] CurY { get; }

        public int[] OrgX { get; }

        public int[] OrgY { get; }

        public bool[] OnCurve { get; }

        public byte[] Touch { get; }

        public int[] ContourEnds { get; set; }

        public int NumContours { get; set; }

        public int PhantomStart { get; set; }
    }

    private struct GraphicsState
    {
        public short ProjX;
        public short ProjY;
        public short FreeX;
        public short FreeY;
        public short DualX;
        public short DualY;
        public int FdotP;
        public int Rp0;
        public int Rp1;
        public int Rp2;
        public int Zp0;
        public int Zp1;
        public int Zp2;
        public int Loop;
        public int MinDistance;
        public int ControlValueCutIn;
        public int SingleWidthValue;
        public int SingleWidthCutIn;
        public int DeltaBase;
        public int DeltaShift;
        public bool AutoFlip;
        public int InstructControl;
        public int ScanControl;
        public int ScanType;
        public RoundMode RoundMode;
        public int Period;
        public int Phase;
        public int Threshold;

        public static GraphicsState Default() => new GraphicsState
        {
            ProjX = 0x4000,
            ProjY = 0,
            FreeX = 0x4000,
            FreeY = 0,
            DualX = 0x4000,
            DualY = 0,
            FdotP = 0x4000,
            Zp0 = 1,
            Zp1 = 1,
            Zp2 = 1,
            Loop = 1,
            MinDistance = 64,
            ControlValueCutIn = 68, // 17/16 px in 26.6
            DeltaBase = 9,
            DeltaShift = 3,
            AutoFlip = true,
            RoundMode = RoundMode.Grid,
            Period = 64,
            Phase = 0,
            Threshold = 32,
        };
    }
}
