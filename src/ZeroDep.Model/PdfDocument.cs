using System;
using System.Collections.Generic;
using System.IO;
using ZeroDep.Abstractions;
using ZeroDep.Objects;

namespace ZeroDep.Model;

/// <summary>
/// A parsed PDF document: the catalog plus an ordered list of pages with inherited
/// attributes (MediaBox, Resources, Rotate) resolved per ISO 32000-2 §7.7.3.4.
/// </summary>
internal sealed class PdfDocument : IDisposable
{
    private const int MaxPageTreeDepth = 64;

    private readonly PdfFile _file;

    private PdfDocument(PdfFile file, PdfDictionary catalog, IReadOnlyList<PdfPage> pages)
    {
        _file = file;
        Catalog = catalog;
        Pages = pages;
    }

    /// <summary>The document catalog.</summary>
    public PdfDictionary Catalog { get; }

    /// <summary>The pages in document order.</summary>
    public IReadOnlyList<PdfPage> Pages { get; }

    /// <summary>The number of pages.</summary>
    public int PageCount => Pages.Count;

    /// <summary>Resolves an indirect reference against the underlying file.</summary>
    public PdfObject Resolve(PdfObject value) => _file.Resolve(value);

    /// <summary>Whether the document is encrypted.</summary>
    public bool IsEncrypted => _file.IsEncrypted;

    /// <summary>Whether the encryption handler is supported (Standard).</summary>
    public bool HandlerSupported => _file.HandlerSupported;

    /// <summary>The cipher in effect.</summary>
    public EncryptionAlgorithm Algorithm => _file.Algorithm;

    /// <summary>The security-handler revision.</summary>
    public int EncryptionRevision => _file.EncryptionRevision;

    /// <summary>Which password authenticated, or Failed / NotRequired.</summary>
    public AuthenticationResult Authentication => _file.Authentication;

    /// <summary>Whether metadata is encrypted.</summary>
    public bool EncryptMetadata => _file.EncryptMetadata;

    /// <summary>The permission flags.</summary>
    public int Permissions => _file.Permissions;

    /// <summary>Opens a document and builds its page list.</summary>
    public static PdfDocument Open(Stream stream, string? password = null)
    {
        PdfFile file = PdfFile.Open(stream, password);
        try
        {
            if (file.IsEncrypted && file.Authentication == AuthenticationResult.Failed)
            {
                throw new PdfSyntaxException("Encrypted document could not be decrypted; a password is required (authentication failed).");
            }
            if (file.Resolve(file.Trailer["Root"] ?? PdfNull.Instance) is not PdfDictionary catalog)
            {
                throw new PdfSyntaxException("Document catalog (/Root) was not found.");
            }
            if (file.Resolve(catalog["Pages"] ?? PdfNull.Instance) is not PdfDictionary pagesRoot)
            {
                throw new PdfSyntaxException("Page tree root (/Pages) was not found.");
            }

            var pages = new List<PdfPage>();
            Traverse(file, pagesRoot, default, new HashSet<int>(), pages, 0);
            return new PdfDocument(file, catalog, pages);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static void Traverse(PdfFile file, PdfDictionary node, Inherited inherited, HashSet<int> visited, List<PdfPage> pages, int depth)
    {
        if (depth > MaxPageTreeDepth) return;

        Inherited effective = inherited.Merge(file, node);
        PdfObject? kidsObj = node["Kids"];

        if (kidsObj is not null && file.Resolve(kidsObj) is PdfArray kids)
        {
            foreach (PdfObject kid in kids.Items)
            {
                if (kid is PdfReference reference && !visited.Add(reference.ObjectNumber)) continue;
                if (file.Resolve(kid) is PdfDictionary child)
                {
                    Traverse(file, child, effective, visited, pages, depth + 1);
                }
            }
        }
        else
        {
            pages.Add(new PdfPage(
                pages.Count,
                node,
                effective.MediaBox ?? PdfRectangle.Default,
                effective.Rotation,
                effective.Resources));
        }
    }

    public void Dispose() => _file.Dispose();

    private readonly struct Inherited
    {
        private Inherited(PdfRectangle? mediaBox, int rotation, PdfDictionary? resources)
        {
            MediaBox = mediaBox;
            Rotation = rotation;
            Resources = resources;
        }

        public PdfRectangle? MediaBox { get; }

        public int Rotation { get; }

        public PdfDictionary? Resources { get; }

        public Inherited Merge(PdfFile file, PdfDictionary node)
        {
            PdfRectangle? mediaBox = MediaBox;
            int rotation = Rotation;
            PdfDictionary? resources = Resources;

            if (node["MediaBox"] is PdfObject mb && file.Resolve(mb) is PdfArray rect && rect.Count >= 4)
            {
                mediaBox = RectangleFrom(file, rect);
            }
            if (node["Rotate"] is PdfObject ro && file.Resolve(ro) is PdfInteger rotate)
            {
                rotation = Normalize((int)rotate.Value);
            }
            if (node["Resources"] is PdfObject rs && file.Resolve(rs) is PdfDictionary res)
            {
                resources = res;
            }

            return new Inherited(mediaBox, rotation, resources);
        }

        private static PdfRectangle RectangleFrom(PdfFile file, PdfArray array)
        {
            double Value(int i) => file.Resolve(array[i]) is PdfNumber n ? n.AsDouble : 0;
            return new PdfRectangle(Value(0), Value(1), Value(2), Value(3));
        }

        private static int Normalize(int rotation) => ((rotation % 360) + 360) % 360;
    }
}
