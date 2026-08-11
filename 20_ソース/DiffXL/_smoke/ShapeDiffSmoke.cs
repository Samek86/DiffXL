using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DiffXL.LOGIC.Diff;

/// <summary>
/// 図形抽出（ExtractShapes）と ShapeCompareService のスモーク。
/// 1) 最小 xlsx から 1 図形を抽出（テキスト・Kind・順序）
/// 2) 片側 1 件 → ShapeOnlyLeft ちょうど 1
/// 3) 同テキスト Match で差分なし / 異テキスト → Shape
/// </summary>
class Program
{
    static int Main()
    {
        int fail = 0;
        string dir = Path.Combine(
            Path.GetTempPath(),
            "diffxl_shape_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);

        try
        {
            // --- 1: 抽出 → 順序リスト（片側 1 件の xlsx） ---
            string xlsx = Path.Combine(dir, "one_shape.xlsx");
            WriteMinimalXlsxWithShape(xlsx, "HelloShape", "rect", row0: 2, col0: 1);
            string cache = Path.Combine(dir, "cache");
            Directory.CreateDirectory(cache);

            using (XlsxPackageReader reader = XlsxPackageReader.Open(xlsx))
            {
                IReadOnlyList<string> sheets = reader.GetSheetNames();
                if (sheets == null || sheets.Count == 0)
                {
                    Console.WriteLine("FAIL case1 no sheets");
                    return 1;
                }

                string sheet = sheets[0];
                IList<ShapeContent> shapes = reader.ExtractShapes(sheet, cache);
                Console.WriteLine("case1 extract count=" + shapes.Count + " sheet=" + sheet);
                foreach (ShapeContent s in shapes)
                {
                    Console.WriteLine(string.Format(
                        "  [{0}] Id={1} Kind={2} Text={3} Hash={4} Anchor={5}",
                        s.OrderIndex,
                        s.Id,
                        s.Kind,
                        Quote(s.Text),
                        s.ContentHash != null && s.ContentHash.Length > 12
                            ? s.ContentHash.Substring(0, 12) + "…"
                            : s.ContentHash,
                        s.Anchor != null ? s.Anchor.ToString() : "(null)"));
                }

                if (shapes.Count != 1)
                {
                    Console.WriteLine("FAIL case1 expected 1 shape actual=" + shapes.Count);
                    fail++;
                }
                else if (!string.Equals(shapes[0].Text, "HelloShape", StringComparison.Ordinal))
                {
                    Console.WriteLine("FAIL case1 Text expected HelloShape actual=" + Quote(shapes[0].Text));
                    fail++;
                }
                else if (string.IsNullOrEmpty(shapes[0].Kind))
                {
                    Console.WriteLine("FAIL case1 Kind empty");
                    fail++;
                }
                else if (string.IsNullOrEmpty(shapes[0].ContentHash))
                {
                    Console.WriteLine("FAIL case1 ContentHash empty");
                    fail++;
                }
                else if (shapes[0].OrderIndex != 0)
                {
                    Console.WriteLine("FAIL case1 OrderIndex expected 0");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case1 extract 1 shape Text=HelloShape Kind=" + shapes[0].Kind);
                }

                // 全シート指定 null でも同数
                IList<ShapeContent> all = reader.ExtractShapes(null, cache);
                if (all.Count != shapes.Count)
                {
                    Console.WriteLine("FAIL case1 null sheetName count " + all.Count + " vs " + shapes.Count);
                    fail++;
                }
            }

            // --- 2: 片側 1 件 → ShapeOnlyLeft ---
            {
                var left = new List<ShapeContent>
                {
                    Shape("S0", "rect", "OnlyLeft", hash: "h-left", row: 3)
                };
                var right = new List<ShapeContent>();
                var pair = new SheetPair { LeftSheet = "Sheet1", RightSheet = "Sheet1" };

                IList<DiffItem> items = ShapeCompareService.Compare(left, right, pair);
                Console.WriteLine("case2 items=" + items.Count);
                Dump(items);

                int onlyL = items.Count(i => i.Kind == DiffKind.ShapeOnlyLeft);
                int onlyR = items.Count(i => i.Kind == DiffKind.ShapeOnlyRight);
                int shape = items.Count(i => i.Kind == DiffKind.Shape);
                if (onlyL != 1 || onlyR != 0 || shape != 0 || items.Count != 1)
                {
                    Console.WriteLine("FAIL case2 expected ShapeOnlyLeft×1 only");
                    fail++;
                }
                else if (items[0].Summary == null
                    || items[0].Summary.IndexOf("OnlyLeft", StringComparison.Ordinal) < 0)
                {
                    Console.WriteLine("FAIL case2 summary should mention OnlyLeft");
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case2 one-sided ShapeOnlyLeft×1");
                }
            }

            // --- 3: 同内容 0 差分 / 異テキスト Shape ---
            {
                var pair = new SheetPair { LeftSheet = "A", RightSheet = "B" };
                var sameL = new List<ShapeContent>
                {
                    Shape("L0", "rect", "Same", hash: "h-same", row: 1)
                };
                var sameR = new List<ShapeContent>
                {
                    Shape("R0", "rect", "Same", hash: "h-same", row: 99)
                };
                IList<DiffItem> sameItems = ShapeCompareService.Compare(sameL, sameR, pair);
                if (sameItems.Count != 0)
                {
                    Console.WriteLine("FAIL case3 same content should have 0 diffs actual=" + sameItems.Count);
                    fail++;
                }
                else
                {
                    Console.WriteLine("OK case3 same content 0 diffs (position ignored)");
                }

                var diffL = new List<ShapeContent>
                {
                    Shape("L0", "rect", "Alpha", hash: "h-a", row: 1)
                };
                var diffR = new List<ShapeContent>
                {
                    Shape("R0", "rect", "Beta", hash: "h-b", row: 1)
                };
                IList<DiffItem> diffItems = ShapeCompareService.Compare(diffL, diffR, pair);
                Dump(diffItems);
                int shapeDiff = diffItems.Count(i => i.Kind == DiffKind.Shape);
                // テキストが違うが Kind 同一 → Match + Shape、または Skip 両方
                // 類似度が閾値以上なら Shape×1 を期待
                if (shapeDiff == 1 && diffItems.Count == 1)
                {
                    Console.WriteLine("OK case3 different text → Shape×1");
                }
                else if (diffItems.Count(i =>
                    i.Kind == DiffKind.ShapeOnlyLeft || i.Kind == DiffKind.ShapeOnlyRight) == 2)
                {
                    // 閾値未満で Skip になった場合も内容差として許容しない：Kind 同一なので Match したい
                    Console.WriteLine("FAIL case3 expected Match+Shape for same Kind different Text");
                    fail++;
                }
                else
                {
                    Console.WriteLine("FAIL case3 unexpected diffs count=" + diffItems.Count
                        + " shape=" + shapeDiff);
                    fail++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL exception: " + ex);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // ignore
            }
        }

        if (fail > 0)
        {
            Console.WriteLine("FAIL ShapeDiffSmoke fails=" + fail);
            return 1;
        }

        Console.WriteLine("PASS ShapeDiffSmoke");
        return 0;
    }

    static ShapeContent Shape(string id, string kind, string text, string hash, int row)
    {
        return new ShapeContent
        {
            Id = id,
            Kind = kind,
            Text = text,
            ContentHash = hash,
            OrderIndex = 0,
            Anchor = new AnchorRect
            {
                RowStart = row,
                RowEnd = row,
                ColStart = 1,
                ColEnd = 2
            }
        };
    }

    static void Dump(IList<DiffItem> items)
    {
        foreach (DiffItem i in items)
        {
            Console.WriteLine(string.Format(
                "  {0} L={1} R={2} {3}",
                i.Kind,
                i.AddressLeft ?? "-",
                i.AddressRight ?? "-",
                i.Summary));
        }
    }

    static string Quote(string s)
    {
        return "\"" + (s ?? "") + "\"";
    }

    /// <summary>
    /// テキスト付き矩形 sp を 1 つ持つ最小 xlsx を ZIP で書く。
    /// </summary>
    static void WriteMinimalXlsxWithShape(
        string path,
        string shapeText,
        string prst,
        int row0,
        int col0)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using (FileStream fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "[Content_Types].xml",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/drawings/drawing1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.drawing+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");

            WriteEntry(zip, "_rels/.rels",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

            WriteEntry(zip, "xl/workbook.xml",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""
 xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Sheet1"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");

            WriteEntry(zip, "xl/_rels/workbook.xml.rels",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");

            WriteEntry(zip, "xl/styles.xml",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""1""><font><sz val=""11""/><color theme=""1""/><name val=""Calibri""/></font></fonts>
  <fills count=""1""><fill><patternFill patternType=""none""/></fill></fills>
  <borders count=""1""><border/></borders>
  <cellStyleXfs count=""1""><xf/></cellStyleXfs>
  <cellXfs count=""1""><xf/></cellXfs>
</styleSheet>");

            WriteEntry(zip, "xl/worksheets/sheet1.xml",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""
 xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheetData>
    <row r=""1""><c r=""A1"" t=""inlineStr""><is><t>cell</t></is></c></row>
  </sheetData>
  <drawing r:id=""rId1""/>
</worksheet>");

            WriteEntry(zip, "xl/worksheets/_rels/sheet1.xml.rels",
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"" Target=""../drawings/drawing1.xml""/>
</Relationships>");

            int row1 = row0 + 3;
            int col1 = col0 + 2;
            string drawing = string.Format(
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<xdr:wsDr xmlns:xdr=""http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing""
 xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">
  <xdr:twoCellAnchor>
    <xdr:from>
      <xdr:col>{0}</xdr:col><xdr:colOff>0</xdr:colOff>
      <xdr:row>{1}</xdr:row><xdr:rowOff>0</xdr:rowOff>
    </xdr:from>
    <xdr:to>
      <xdr:col>{2}</xdr:col><xdr:colOff>0</xdr:colOff>
      <xdr:row>{3}</xdr:row><xdr:rowOff>0</xdr:rowOff>
    </xdr:to>
    <xdr:sp macro="""" textlink="""">
      <xdr:nvSpPr>
        <xdr:cNvPr id=""2"" name=""Rect 1""/>
        <xdr:cNvSpPr txBox=""0""/>
      </xdr:nvSpPr>
      <xdr:spPr>
        <a:xfrm>
          <a:off x=""0"" y=""0""/>
          <a:ext cx=""1200000"" cy=""800000""/>
        </a:xfrm>
        <a:prstGeom prst=""{4}""><a:avLst/></a:prstGeom>
        <a:solidFill><a:srgbClr val=""5B9BD5""/></a:solidFill>
        <a:ln><a:noFill/></a:ln>
      </xdr:spPr>
      <xdr:txBody>
        <a:bodyPr/><a:lstStyle/>
        <a:p><a:r><a:rPr lang=""en-US"" sz=""1100""/><a:t>{5}</a:t></a:r></a:p>
      </xdr:txBody>
    </xdr:sp>
    <xdr:clientData/>
  </xdr:twoCellAnchor>
</xdr:wsDr>",
                col0, row0, col1, row1, prst, EscapeXml(shapeText));

            WriteEntry(zip, "xl/drawings/drawing1.xml", drawing);
        }
    }

    static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (Stream s = e.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write(content);
        }
    }

    static string EscapeXml(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
