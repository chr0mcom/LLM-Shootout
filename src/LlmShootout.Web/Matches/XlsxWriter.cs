using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace LlmShootout.Web.Matches;

/// <summary>A sheet of a workbook. Cells are strings, numbers, <see cref="Money"/> or null.</summary>
internal sealed record Sheet(string Name, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<double> Widths);

/// <summary>A number shown with four decimals.</summary>
internal readonly record struct Money(decimal Value);

/// <summary>
/// Writes a plain .xlsx file (a zip of a few XML parts) without any library: a bold header row that stays in view,
/// wrapped text, column widths. Text from models is cleaned of characters that XML cannot carry.
/// </summary>
internal static class XlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MaxCellLength = 32000;
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Write(IReadOnlyList<Sheet> sheets)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
            Add(zip, "_rels/.rels", RootRelationships());
            Add(zip, "xl/workbook.xml", Workbook(sheets));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
            Add(zip, "xl/styles.xml", Styles());
            for (var i = 0; i < sheets.Count; i++)
            {
                Add(zip, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i]));
            }
        }

        return stream.ToArray();
    }

    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaxCellLength));
        for (var i = 0; i < text.Length && builder.Length < MaxCellLength; i++)
        {
            if (XmlConvert.IsXmlChar(text[i]))
            {
                builder.Append(text[i]);
            }
            else if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i]))
            {
                builder.Append(text[i]).Append(text[++i]);
            }
        }

        return builder.ToString();
    }

    internal static string Column(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static string SheetName(string name)
    {
        var clean = new string([.. name.Where(c => !"[]:*?/\\".Contains(c, StringComparison.Ordinal))]);
        return clean.Length == 0 ? "Sheet" : clean[..Math.Min(clean.Length, 31)];
    }

    private static void Add(ZipArchive zip, string path, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var xml = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        xml.WriteStartDocument(standalone: true);
        write(xml);
    }

    private static Action<XmlWriter> ContentTypes(int sheets) => xml =>
    {
        const string Ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        xml.WriteStartElement("Types", Ns);
        Default(xml, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(xml, "xml", "application/xml");
        Override(xml, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(xml, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var i = 1; i <= sheets; i++)
        {
            Override(xml, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        }

        xml.WriteEndElement();
    };

    private static void Default(XmlWriter xml, string extension, string type)
    {
        xml.WriteStartElement("Default");
        xml.WriteAttributeString("Extension", extension);
        xml.WriteAttributeString("ContentType", type);
        xml.WriteEndElement();
    }

    private static void Override(XmlWriter xml, string part, string type)
    {
        xml.WriteStartElement("Override");
        xml.WriteAttributeString("PartName", part);
        xml.WriteAttributeString("ContentType", type);
        xml.WriteEndElement();
    }

    private static Action<XmlWriter> RootRelationships() => xml =>
    {
        xml.WriteStartElement("Relationships", PackageRelationships);
        Relationship(xml, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
        xml.WriteEndElement();
    };

    private static Action<XmlWriter> WorkbookRelationships(int sheets) => xml =>
    {
        xml.WriteStartElement("Relationships", PackageRelationships);
        for (var i = 1; i <= sheets; i++)
        {
            Relationship(xml, $"rId{i}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i}.xml");
        }

        Relationship(xml, $"rId{sheets + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
        xml.WriteEndElement();
    };

    private static void Relationship(XmlWriter xml, string id, string type, string target)
    {
        xml.WriteStartElement("Relationship");
        xml.WriteAttributeString("Id", id);
        xml.WriteAttributeString("Type", type);
        xml.WriteAttributeString("Target", target);
        xml.WriteEndElement();
    }

    private static Action<XmlWriter> Workbook(IReadOnlyList<Sheet> sheets) => xml =>
    {
        xml.WriteStartElement("workbook", Main);
        xml.WriteAttributeString("xmlns", "r", null, Relationships);
        xml.WriteStartElement("sheets");
        for (var i = 0; i < sheets.Count; i++)
        {
            xml.WriteStartElement("sheet");
            xml.WriteAttributeString("name", SheetName(sheets[i].Name));
            xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("id", Relationships, $"rId{i + 1}");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    };

    private static Action<XmlWriter> Styles() => xml =>
    {
        xml.WriteStartElement("styleSheet", Main);
        xml.WriteRaw("<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.0000\"/></numFmts>");
        xml.WriteRaw("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>");
        xml.WriteRaw("<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDDE3EA\"/></patternFill></fill></fills>");
        xml.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        xml.WriteRaw("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        xml.WriteRaw("<cellXfs count=\"4\">"
            + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>"
            + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>"
            + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>"
            + "</cellXfs>");
        xml.WriteRaw("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        xml.WriteEndElement();
    };

    private static Action<XmlWriter> Worksheet(Sheet sheet) => xml =>
    {
        xml.WriteStartElement("worksheet", Main);
        xml.WriteRaw("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.WriteStartElement("cols");
        for (var i = 0; i < sheet.Header.Count; i++)
        {
            xml.WriteStartElement("col");
            xml.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("width", (i < sheet.Widths.Count ? sheet.Widths[i] : 16).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("customWidth", "1");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteStartElement("sheetData");
        WriteRow(xml, 1, sheet.Header.Cast<object?>().ToList(), header: true);
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            WriteRow(xml, r + 2, sheet.Rows[r], header: false);
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    };

    private static void WriteRow(XmlWriter xml, int number, IReadOnlyList<object?> cells, bool header)
    {
        xml.WriteStartElement("row");
        xml.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < cells.Count; i++)
        {
            WriteCell(xml, $"{Column(i)}{number}", cells[i], header);
        }

        xml.WriteEndElement();
    }

    private static void WriteCell(XmlWriter xml, string reference, object? value, bool header)
    {
        if (value is null)
        {
            return;
        }

        xml.WriteStartElement("c");
        xml.WriteAttributeString("r", reference);
        switch (value)
        {
            case int or long or double or float:
                xml.WriteAttributeString("s", "0");
                xml.WriteElementString("v", Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case decimal number:
                xml.WriteAttributeString("s", "0");
                xml.WriteElementString("v", number.ToString(CultureInfo.InvariantCulture));
                break;
            case Money money:
                xml.WriteAttributeString("s", "3");
                xml.WriteElementString("v", money.Value.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteAttributeString("s", header ? "1" : "2");
                xml.WriteStartElement("is");
                xml.WriteStartElement("t");
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteString(Clean(Convert.ToString(value, CultureInfo.InvariantCulture)));
                xml.WriteEndElement();
                xml.WriteEndElement();
                break;
        }

        xml.WriteEndElement();
    }
}
