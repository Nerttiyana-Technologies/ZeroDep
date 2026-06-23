using System.Collections.Generic;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

public sealed class ContentInterpreterTests
{
    private static PdfDictionary Dict(params (string Key, PdfObject Value)[] entries)
    {
        var map = new Dictionary<string, PdfObject>();
        foreach (var (key, value) in entries) map[key] = value;
        return new PdfDictionary(map);
    }

    private static PdfStream Image(int width, int height)
    {
        var dict = Dict(
            ("Type", new PdfName("XObject")),
            ("Subtype", new PdfName("Image")),
            ("Width", new PdfInteger(width)),
            ("Height", new PdfInteger(height)));
        return new PdfStream(dict, new byte[0]);
    }

    private static ContentInterpreter Interpreter()
        => new ContentInterpreter(o => o, s => s.GetRawBytes());

    [Fact]
    public void RecordsImagePlacementWithCtm()
    {
        var resources = Dict(("XObject", Dict(("Im0", Image(100, 50)))));
        byte[] content = Encoding.ASCII.GetBytes("q 100 0 0 50 10 20 cm /Im0 Do Q");

        var placements = Interpreter().Run(content, resources, Matrix.Identity);

        var placement = Assert.Single(placements);
        Assert.Equal("Im0", placement.Name);
        Assert.Equal(100, placement.Transform.A, 3);
        Assert.Equal(50, placement.Transform.D, 3);
        Assert.Equal(10, placement.Transform.E, 3);
        Assert.Equal(20, placement.Transform.F, 3);
    }

    [Fact]
    public void RestoresCtmOnQ()
    {
        var resources = Dict(("XObject", Dict(("Im0", Image(10, 10)))));
        byte[] content = Encoding.ASCII.GetBytes("q 2 0 0 2 0 0 cm /Im0 Do Q /Im0 Do");

        var placements = Interpreter().Run(content, resources, Matrix.Identity);

        Assert.Equal(2, placements.Count);
        Assert.Equal(2, placements[0].Transform.A, 3); // inside q/Q
        Assert.Equal(1, placements[1].Transform.A, 3); // restored to identity
    }

    [Fact]
    public void RecursesIntoFormXObjectApplyingItsMatrix()
    {
        var image = Image(100, 50);
        var formResources = Dict(("XObject", Dict(("Im0", image))));
        var formDict = Dict(
            ("Type", new PdfName("XObject")),
            ("Subtype", new PdfName("Form")),
            ("Matrix", new PdfArray(new PdfObject[]
            {
                new PdfReal(0.5), new PdfInteger(0), new PdfInteger(0),
                new PdfReal(0.5), new PdfInteger(0), new PdfInteger(0),
            })),
            ("Resources", formResources));
        var form = new PdfStream(formDict, Encoding.ASCII.GetBytes("/Im0 Do"));

        var resources = Dict(("XObject", Dict(("F0", form))));
        byte[] content = Encoding.ASCII.GetBytes("/F0 Do");

        var placements = Interpreter().Run(content, resources, Matrix.Identity);

        var placement = Assert.Single(placements);
        Assert.Equal("Im0", placement.Name);
        Assert.Equal(0.5, placement.Transform.A, 3);
        Assert.Equal(0.5, placement.Transform.D, 3);
    }

    [Fact]
    public void CapturesInlineImageAndDoesNotMisparseItsData()
    {
        // The inline image is captured; its binary data ("Do Q") must NOT be parsed as operators,
        // so exactly two placements result: the inline image and the following XObject image.
        var resources = Dict(("XObject", Dict(("Im0", Image(10, 10)))));
        byte[] content = Encoding.ASCII.GetBytes("BI /W 2 /H 2 ID \x01\x02 Do Q EI /Im0 Do");

        var placements = Interpreter().Run(content, resources, Matrix.Identity);

        Assert.Equal(2, placements.Count);
        Assert.Contains(placements, p => p.Name == "inline");
        Assert.Contains(placements, p => p.Name == "Im0");
    }
}
