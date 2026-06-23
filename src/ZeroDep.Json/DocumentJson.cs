using ZeroDep.Abstractions;

namespace ZeroDep.Json;

/// <summary>Serializes a <see cref="DocumentAnalysis"/> to the versioned JSON schema (pure BCL).</summary>
public static class DocumentJson
{
    /// <summary>Writes the document analysis as JSON.</summary>
    /// <param name="analysis">The analysis to serialize.</param>
    /// <param name="indent">Whether to pretty-print.</param>
    public static string Write(DocumentAnalysis analysis, bool indent = false)
    {
        var w = new JsonWriter(indent);
        w.BeginObject();
        w.Property("schemaVersion").Value(analysis.SchemaVersion);
        w.Property("status").Value(analysis.Status.ToString());
        if (analysis.Rejection is RejectionInfo rejection)
        {
            w.Property("rejection").BeginObject();
            w.Property("reason").Value(rejection.Reason.ToString());
            w.Property("detail").Value(rejection.Detail);
            w.EndObject();
        }
        else
        {
            w.Property("rejection").Null();
        }
        w.Property("pageCount").Value(analysis.PageCount);
        w.Property("imageAreaFraction").Value(analysis.ImageAreaFraction);

        SecurityInfo security = analysis.Security;
        w.Property("security").BeginObject();
        w.Property("isEncrypted").Value(security.IsEncrypted);
        w.Property("handlerSupported").Value(security.HandlerSupported);
        w.Property("algorithm").Value(AlgorithmName(security.Algorithm));
        w.Property("revision").Value(security.Revision);
        w.Property("authentication").Value(security.Authentication.ToString());
        w.Property("metadataEncrypted").Value(security.MetadataEncrypted);
        w.Property("permissions").Value(security.Permissions);
        w.EndObject();

        w.Property("images").BeginArray();
        foreach (ImageDpiInfo image in analysis.Images)
        {
            w.BeginObject();
            w.Property("page").Value(image.PageIndex);
            w.Property("resourceName").Value(image.ResourceName);
            w.Property("pixelWidth").Value(image.PixelWidth);
            w.Property("pixelHeight").Value(image.PixelHeight);
            w.Property("renderedWidthPoints").Value(image.RenderedWidthPoints);
            w.Property("renderedHeightPoints").Value(image.RenderedHeightPoints);
            w.Property("horizontalDpi").Value(image.HorizontalDpi);
            w.Property("verticalDpi").Value(image.VerticalDpi);
            w.Property("effectiveDpi").Value(image.EffectiveDpi);
            w.Property("threshold").Value(image.Threshold);
            w.Property("belowThreshold").Value(image.IsBelowThreshold);
            w.Property("filter").Value(image.Filter);
            w.Property("hasSoftMask").Value(image.HasSoftMask);
            w.Property("hasMask").Value(image.HasMask);
            w.Property("isImageMask").Value(image.IsImageMask);
            w.Property("pageAreaFraction").Value(image.PageAreaFraction);
            w.EndObject();
        }
        w.EndArray();

        w.Property("textRuns").BeginArray();
        foreach (TextRunInfo run in analysis.TextRuns)
        {
            w.BeginObject();
            w.Property("page").Value(run.PageIndex);
            w.Property("text").Value(run.Text);
            w.Property("x").Value(run.X);
            w.Property("y").Value(run.Y);
            w.Property("width").Value(run.Width);
            w.Property("fontSize").Value(run.FontSize);
            w.Property("renderMode").Value(run.RenderMode);
            w.Property("isOcrLayer").Value(run.IsOcrLayer);
            w.EndObject();
        }
        w.EndArray();

        w.Property("form").BeginObject();
        w.Property("hasAcroForm").Value(analysis.Form.HasAcroForm);
        w.Property("hasXfa").Value(analysis.Form.HasXfa);
        w.Property("fields").BeginArray();
        foreach (FormFieldInfo field in analysis.Form.Fields)
        {
            w.BeginObject();
            w.Property("fullyQualifiedName").Value(field.FullyQualifiedName);
            w.Property("partialName").Value(field.PartialName);
            w.Property("label").Value(field.Label);
            w.Property("fieldType").Value(field.FieldType);
            w.Property("value").Value(field.Value);
            if (field.IsChecked is bool checkedState) w.Property("isChecked").Value(checkedState);
            else w.Property("isChecked").Null();
            if (field.PageIndex is int page) w.Property("page").Value(page);
            else w.Property("page").Null();
            if (field.Rect is BoundingBox r)
            {
                w.Property("rect").BeginObject();
                w.Property("x").Value(r.X);
                w.Property("y").Value(r.Y);
                w.Property("width").Value(r.Width);
                w.Property("height").Value(r.Height);
                w.EndObject();
            }
            else
            {
                w.Property("rect").Null();
            }
            w.EndObject();
        }
        w.EndArray();
        w.EndObject();

        w.Property("coverage").BeginArray();
        foreach (CoverageItem item in analysis.Coverage)
        {
            w.BeginObject();
            w.Property("id").Value(item.Id);
            w.Property("kind").Value(item.Kind);
            w.Property("value").Value(item.Value);
            w.Property("page").Value(item.Page);
            w.Property("bounds").BeginObject();
            w.Property("x").Value(item.Bounds.X);
            w.Property("y").Value(item.Bounds.Y);
            w.Property("width").Value(item.Bounds.Width);
            w.Property("height").Value(item.Bounds.Height);
            w.EndObject();
            w.EndObject();
        }
        w.EndArray();

        w.EndObject();
        return w.ToString();
    }

    private static string AlgorithmName(EncryptionAlgorithm algorithm)
        => algorithm switch
        {
            EncryptionAlgorithm.Rc4 => "RC4",
            EncryptionAlgorithm.Aes128 => "AES-128",
            EncryptionAlgorithm.Aes256 => "AES-256",
            _ => "None",
        };
}
