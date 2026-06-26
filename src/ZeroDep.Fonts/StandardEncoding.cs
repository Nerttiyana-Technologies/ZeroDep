namespace ZeroDep.Fonts;

/// <summary>
/// Adobe StandardEncoding (code → glyph name). Used by the Type 1 <c>seac</c> operator to resolve the base
/// and accent glyphs of an accented composite, independent of the font's own /Encoding.
/// </summary>
internal static class StandardEncoding
{
    private static readonly string?[] Names = Build();

    /// <summary>Returns the glyph name for a StandardEncoding code, or <c>null</c> if unassigned.</summary>
    public static string? Name(int code) => code >= 0 && code < Names.Length ? Names[code] : null;

    private static string?[] Build()
    {
        var n = new string?[256];
        n[32] = "space"; n[33] = "exclam"; n[34] = "quotedbl"; n[35] = "numbersign";
        n[36] = "dollar"; n[37] = "percent"; n[38] = "ampersand"; n[39] = "quoteright";
        n[40] = "parenleft"; n[41] = "parenright"; n[42] = "asterisk"; n[43] = "plus";
        n[44] = "comma"; n[45] = "hyphen"; n[46] = "period"; n[47] = "slash";
        n[48] = "zero"; n[49] = "one"; n[50] = "two"; n[51] = "three"; n[52] = "four";
        n[53] = "five"; n[54] = "six"; n[55] = "seven"; n[56] = "eight"; n[57] = "nine";
        n[58] = "colon"; n[59] = "semicolon"; n[60] = "less"; n[61] = "equal";
        n[62] = "greater"; n[63] = "question"; n[64] = "at";
        n[65] = "A"; n[66] = "B"; n[67] = "C"; n[68] = "D"; n[69] = "E"; n[70] = "F";
        n[71] = "G"; n[72] = "H"; n[73] = "I"; n[74] = "J"; n[75] = "K"; n[76] = "L";
        n[77] = "M"; n[78] = "N"; n[79] = "O"; n[80] = "P"; n[81] = "Q"; n[82] = "R";
        n[83] = "S"; n[84] = "T"; n[85] = "U"; n[86] = "V"; n[87] = "W"; n[88] = "X";
        n[89] = "Y"; n[90] = "Z";
        n[91] = "bracketleft"; n[92] = "backslash"; n[93] = "bracketright";
        n[94] = "asciicircum"; n[95] = "underscore"; n[96] = "quoteleft";
        n[97] = "a"; n[98] = "b"; n[99] = "c"; n[100] = "d"; n[101] = "e"; n[102] = "f";
        n[103] = "g"; n[104] = "h"; n[105] = "i"; n[106] = "j"; n[107] = "k"; n[108] = "l";
        n[109] = "m"; n[110] = "n"; n[111] = "o"; n[112] = "p"; n[113] = "q"; n[114] = "r";
        n[115] = "s"; n[116] = "t"; n[117] = "u"; n[118] = "v"; n[119] = "w"; n[120] = "x";
        n[121] = "y"; n[122] = "z";
        n[123] = "braceleft"; n[124] = "bar"; n[125] = "braceright"; n[126] = "asciitilde";
        n[161] = "exclamdown"; n[162] = "cent"; n[163] = "sterling"; n[164] = "fraction";
        n[165] = "yen"; n[166] = "florin"; n[167] = "section"; n[168] = "currency";
        n[169] = "quotesingle"; n[170] = "quotedblleft"; n[171] = "guillemotleft";
        n[172] = "guilsinglleft"; n[173] = "guilsinglright"; n[174] = "fi"; n[175] = "fl";
        n[177] = "endash"; n[178] = "dagger"; n[179] = "daggerdbl"; n[180] = "periodcentered";
        n[182] = "paragraph"; n[183] = "bullet"; n[184] = "quotesinglbase";
        n[185] = "quotedblbase"; n[186] = "quotedblright"; n[187] = "guillemotright";
        n[188] = "ellipsis"; n[189] = "perthousand"; n[191] = "questiondown";
        n[193] = "grave"; n[194] = "acute"; n[195] = "circumflex"; n[196] = "tilde";
        n[197] = "macron"; n[198] = "breve"; n[199] = "dotaccent"; n[200] = "dieresis";
        n[202] = "ring"; n[203] = "cedilla"; n[205] = "hungarumlaut"; n[206] = "ogonek";
        n[207] = "caron"; n[208] = "emdash"; n[225] = "AE"; n[227] = "ordfeminine";
        n[232] = "Lslash"; n[233] = "Oslash"; n[234] = "OE"; n[235] = "ordmasculine";
        n[241] = "ae"; n[245] = "dotlessi"; n[248] = "lslash"; n[249] = "oslash";
        n[250] = "oe"; n[251] = "germandbls";
        return n;
    }
}
