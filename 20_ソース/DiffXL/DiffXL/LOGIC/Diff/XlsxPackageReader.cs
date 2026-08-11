using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Diff
{
    /// <summary>
    /// .xlsx（ZIP）からシート名・セル値・埋め込み画像を読み取る。
    /// </summary>
    public sealed class XlsxPackageReader : IDisposable
    {
        /// <summary>
        /// スプレッドシート ML 名前空間。
        /// </summary>
        private static readonly XNamespace NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>
        /// パッケージ関係名前空間。
        /// </summary>
        private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>
        /// Office ドキュメント関係名前空間。
        /// </summary>
        private static readonly XNamespace NsOfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary>
        /// SpreadsheetML Drawing 名前空間。
        /// </summary>
        private static readonly XNamespace NsXdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        /// <summary>
        /// DrawingML メイン名前空間。
        /// </summary>
        private static readonly XNamespace NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";

        /// <summary>
        /// 関係属性用 r 名前空間。
        /// </summary>
        private static readonly XNamespace NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary>
        /// セルアドレス解析。
        /// </summary>
        private static readonly Regex CellRefRegex = new Regex(
            @"^([A-Za-z]+)(\d+)$",
            RegexOptions.Compiled);

        /// <summary>
        /// 開いている ZIP。
        /// </summary>
        private ZipArchive _zip;

        /// <summary>
        /// 元ファイルパス。
        /// </summary>
        private readonly string _path;

        /// <summary>
        /// 共有文字列テーブル。
        /// </summary>
        private List<string> _sharedStrings;

        /// <summary>
        /// シート名 → パッケージ内シート XML パス。
        /// </summary>
        private Dictionary<string, string> _sheetPaths;

        /// <summary>
        /// cellXfs インデックス → 背景 ARGB（#AARRGGBB）。塗りなし・解決不能は null。
        /// </summary>
        private string[] _xfBackgroundArgb;

        /// <summary>
        /// cellXfs インデックス → 四辺いずれかにボーダーがあるか。
        /// </summary>
        private bool[] _xfHasAnyBorder;

        /// <summary>
        /// theme 色（0=dk1 … 11=folHlink）。#AARRGGBB。
        /// </summary>
        private string[] _themeColors;

        /// <summary>
        /// indexed 色パレット（#AARRGGBB）。
        /// </summary>
        private string[] _indexedColors;

        /// <summary>
        /// 破棄済み。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// コンストラクタ（Open を使う）。
        /// </summary>
        private XlsxPackageReader(string path, ZipArchive zip)
        {
            _path = path;
            _zip = zip;
        }

        /// <summary>
        /// .xlsx を開く。
        /// </summary>
        /// <param name="path">ファイルパス</param>
        /// <returns>リーダー</returns>
        public static XlsxPackageReader Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("パスが空です。", nameof(path));
            }

            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("ファイルが見つかりません。", full);
            }

            if (!string.Equals(Path.GetExtension(full), Common.ExcelExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("対象形式は .xlsx のみです: " + full);
            }

            ZipArchive zip = ZipFile.OpenRead(full);
            var reader = new XlsxPackageReader(full, zip);
            try
            {
                reader.LoadWorkbookStructure();
                return reader;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        /// <summary>
        /// シート名一覧（ブック順）。
        /// </summary>
        public IReadOnlyList<string> GetSheetNames()
        {
            EnsureOpen();
            return _sheetPaths.Keys.ToList();
        }

        /// <summary>
        /// 指定シートのセルを列挙する（テキスト互換。背景・ボーダーは含まない）。
        /// </summary>
        /// <param name="sheetName">シート名</param>
        /// <returns>セル値</returns>
        public IEnumerable<CellValue> EnumerateCells(string sheetName)
        {
            foreach (CellContent c in EnumerateCellContents(sheetName))
            {
                yield return new CellValue
                {
                    Address = c.Address,
                    Text = c.Text,
                    Row = c.Row,
                    Column = c.Column
                };
            }
        }

        /// <summary>
        /// 指定シートのセル内容（テキスト・背景色・ボーダー有無）を列挙する。
        /// </summary>
        /// <param name="sheetName">シート名</param>
        /// <returns>セル内容</returns>
        public IEnumerable<CellContent> EnumerateCellContents(string sheetName)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(sheetName) || !_sheetPaths.ContainsKey(sheetName))
            {
                yield break;
            }

            string sheetPath = _sheetPaths[sheetName];
            XDocument doc = ReadXmlEntry(sheetPath);
            if (doc == null)
            {
                yield break;
            }

            foreach (XElement c in doc.Descendants(NsMain + "c"))
            {
                string address = (string)c.Attribute("r");
                if (string.IsNullOrEmpty(address))
                {
                    continue;
                }

                string text = ReadCellText(c);
                int row;
                int col;
                ParseAddress(address, out row, out col);

                int styleIndex = 0;
                string sAttr = (string)c.Attribute("s");
                if (!string.IsNullOrEmpty(sAttr))
                {
                    int.TryParse(sAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out styleIndex);
                }

                string bg = null;
                bool hasBorder = false;
                if (_xfBackgroundArgb != null && styleIndex >= 0 && styleIndex < _xfBackgroundArgb.Length)
                {
                    bg = _xfBackgroundArgb[styleIndex];
                }

                if (_xfHasAnyBorder != null && styleIndex >= 0 && styleIndex < _xfHasAnyBorder.Length)
                {
                    hasBorder = _xfHasAnyBorder[styleIndex];
                }

                yield return new CellContent
                {
                    Address = address,
                    Text = text ?? string.Empty,
                    Row = row,
                    Column = col,
                    BackgroundArgb = bg,
                    HasAnyBorder = hasBorder
                };
            }
        }

        /// <summary>
        /// 埋め込み画像を cacheDir へ抽出し一覧を返す。
        /// シート紐付けが取れるものは SheetName を設定。取れない場合は全体リストとして返す。
        /// </summary>
        /// <param name="sheetName">対象シート（null なら全 media）</param>
        /// <param name="cacheDir">抽出先ディレクトリ</param>
        /// <returns>画像一覧</returns>
        public IReadOnlyList<EmbeddedImage> ExtractImages(string sheetName, string cacheDir)
        {
            EnsureOpen();
            Directory.CreateDirectory(cacheDir);

            // 全 media を抽出
            var allMedia = new List<EmbeddedImage>();
            foreach (ZipArchiveEntry entry in _zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("/"))
                {
                    continue;
                }

                string fileName = Path.GetFileName(name);
                string dest = Path.Combine(cacheDir, fileName);
                // 同名衝突回避
                if (File.Exists(dest))
                {
                    dest = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(fileName)
                        + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                        + Path.GetExtension(fileName));
                }

                entry.ExtractToFile(dest, true);
                int pw = 0;
                int ph = 0;
                long fsize = 0;
                try
                {
                    fsize = new FileInfo(dest).Length;
                    TryReadImageSize(dest, out pw, out ph);
                }
                catch (Exception ex)
                {
                    Log.Debug("画像メタ取得スキップ: " + dest + " " + ex.Message);
                }

                allMedia.Add(new EmbeddedImage
                {
                    PackagePath = name,
                    FileName = Path.GetFileName(dest),
                    ExtractedPath = dest,
                    ContentHash = ComputeFileHash(dest),
                    PixelWidth = pw,
                    PixelHeight = ph,
                    FileSizeBytes = fsize
                });
            }

            if (allMedia.Count == 0)
            {
                return allMedia;
            }

            // シートごとの drawing 関連付け + アンカー行を試行
            Dictionary<string, List<DrawingMediaAnchor>> sheetToAnchors = TryMapSheetMediaAnchors();
            if (sheetToAnchors.Count == 0)
            {
                Log.Debug("画像とシートの関連付けが取れませんでした。ブック単位で扱います: " + _path);
                if (string.IsNullOrEmpty(sheetName))
                {
                    return allMedia;
                }

                // シート指定時もフォールバックとして全件返す（呼び出し側で重複比較に注意）
                return allMedia;
            }

            // PackagePath → 画像のインデックス（同名 media が複数アンカーされることは稀）
            var mediaByPath = new Dictionary<string, EmbeddedImage>(StringComparer.OrdinalIgnoreCase);
            foreach (EmbeddedImage img in allMedia)
            {
                string key = NormalizePackagePath(img.PackagePath);
                if (!mediaByPath.ContainsKey(key))
                {
                    mediaByPath[key] = img;
                }
            }

            if (string.IsNullOrEmpty(sheetName))
            {
                foreach (var kv in sheetToAnchors)
                {
                    foreach (DrawingMediaAnchor anchor in kv.Value)
                    {
                        EmbeddedImage img;
                        if (!mediaByPath.TryGetValue(NormalizePackagePath(anchor.MediaPackagePath), out img))
                        {
                            continue;
                        }

                        // 同一 media が複数シートに現れる場合は最初のシートを優先
                        if (string.IsNullOrEmpty(img.SheetName))
                        {
                            img.SheetName = kv.Key;
                        }

                        if (img.AnchorRow <= 0 && anchor.Row1Based > 0)
                        {
                            ApplyAnchor(img, anchor);
                        }
                    }
                }

                return allMedia;
            }

            List<DrawingMediaAnchor> anchorsForSheet;
            if (!sheetToAnchors.TryGetValue(sheetName, out anchorsForSheet))
            {
                Log.Debug("シート '" + sheetName + "' に紐づく画像がありません。");
                return new List<EmbeddedImage>();
            }

            var result = new List<EmbeddedImage>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DrawingMediaAnchor anchor in anchorsForSheet)
            {
                string key = NormalizePackagePath(anchor.MediaPackagePath);
                EmbeddedImage src;
                if (!mediaByPath.TryGetValue(key, out src))
                {
                    continue;
                }

                // 同じ media を複数アンカーで使う場合は複製（行位置を個別に持てる）
                EmbeddedImage copy;
                if (used.Contains(key))
                {
                    copy = CloneEmbeddedImage(src);
                }
                else
                {
                    copy = src;
                    used.Add(key);
                }

                copy.SheetName = sheetName;
                if (anchor.Row1Based > 0)
                {
                    ApplyAnchor(copy, anchor);
                }

                result.Add(copy);
            }

            return result;
        }

        /// <summary>
        /// DrawingMediaAnchor を EmbeddedImage に反映し、互換の AnchorRow/Column を同期する。
        /// </summary>
        private static void ApplyAnchor(EmbeddedImage img, DrawingMediaAnchor anchor)
        {
            if (img == null || anchor == null || anchor.Row1Based <= 0)
            {
                return;
            }

            int rowEnd = anchor.RowEnd1Based > 0 ? anchor.RowEnd1Based : anchor.Row1Based;
            int colEnd = anchor.ColumnEnd1Based > 0 ? anchor.ColumnEnd1Based : anchor.Column1Based;
            if (rowEnd < anchor.Row1Based)
            {
                rowEnd = anchor.Row1Based;
            }

            if (colEnd < anchor.Column1Based)
            {
                colEnd = anchor.Column1Based;
            }

            img.Anchor = new AnchorRect
            {
                RowStart = anchor.Row1Based,
                RowEnd = rowEnd,
                ColStart = anchor.Column1Based > 0 ? anchor.Column1Based : 1,
                ColEnd = colEnd > 0 ? colEnd : (anchor.Column1Based > 0 ? anchor.Column1Based : 1)
            };
            img.AnchorRow = img.Anchor.RowStart;
            img.AnchorColumn = img.Anchor.ColStart;
        }

        /// <summary>
        /// 埋め込み画像の浅いコピー。
        /// </summary>
        private static EmbeddedImage CloneEmbeddedImage(EmbeddedImage src)
        {
            if (src == null)
            {
                return null;
            }

            return new EmbeddedImage
            {
                PackagePath = src.PackagePath,
                FileName = src.FileName,
                ExtractedPath = src.ExtractedPath,
                SheetName = src.SheetName,
                ContentHash = src.ContentHash,
                PixelWidth = src.PixelWidth,
                PixelHeight = src.PixelHeight,
                FileSizeBytes = src.FileSizeBytes,
                AnchorRow = src.AnchorRow,
                AnchorColumn = src.AnchorColumn,
                Anchor = src.Anchor != null ? src.Anchor.Clone() : null
            };
        }

        /// <summary>
        /// シート drawing から図形（sp / cxnSp。pic は除く）を出現順で抽出する。
        /// 図形内テキストがあれば Text に格納。無ければ Kind+サイズ等の正規化指紋を ContentHash にする。
        /// ラスタ化は行わず RasterPath は null（重い処理を避ける最小実装）。
        /// </summary>
        /// <param name="sheetName">対象シート（null / 空なら全シート）</param>
        /// <param name="cacheDir">キャッシュ先（現状未使用。将来ラスタ用。null 可）</param>
        /// <returns>出現順（行→列）の図形一覧</returns>
        public IList<ShapeContent> ExtractShapes(string sheetName, string cacheDir)
        {
            EnsureOpen();
            if (!string.IsNullOrEmpty(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var result = new List<ShapeContent>();
            try
            {
                IEnumerable<KeyValuePair<string, string>> sheets = _sheetPaths;
                if (!string.IsNullOrEmpty(sheetName))
                {
                    if (!_sheetPaths.ContainsKey(sheetName))
                    {
                        return result;
                    }

                    sheets = new[]
                    {
                        new KeyValuePair<string, string>(sheetName, _sheetPaths[sheetName])
                    };
                }

                foreach (var kv in sheets)
                {
                    string name = kv.Key;
                    string sheetPath = kv.Value;
                    string sheetDir = GetPackageDirectory(sheetPath);
                    string sheetFile = Path.GetFileName(sheetPath);
                    string relsPath = sheetDir + "/_rels/" + sheetFile + ".rels";
                    Dictionary<string, string> sheetRels = LoadRelationships(relsPath);

                    foreach (var rel in sheetRels)
                    {
                        if (rel.Value.IndexOf("drawing", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        string drawingPath = ResolveRelativePackagePath(sheetDir, rel.Value);
                        List<ShapeContent> shapes = ExtractShapesFromDrawing(drawingPath, name);
                        result.AddRange(shapes);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ExtractShapes 失敗: " + ex.Message);
            }

            // 出現順: 行 → 列 → 元順
            result.Sort((a, b) =>
            {
                int ra = a.Anchor != null ? a.Anchor.RowStart : 0;
                int rb = b.Anchor != null ? b.Anchor.RowStart : 0;
                int cmp = ra.CompareTo(rb);
                if (cmp != 0)
                {
                    return cmp;
                }

                int ca = a.Anchor != null ? a.Anchor.ColStart : 0;
                int cb = b.Anchor != null ? b.Anchor.ColStart : 0;
                cmp = ca.CompareTo(cb);
                if (cmp != 0)
                {
                    return cmp;
                }

                return a.OrderIndex.CompareTo(b.OrderIndex);
            });

            for (int i = 0; i < result.Count; i++)
            {
                result[i].OrderIndex = i;
                if (string.IsNullOrEmpty(result[i].Id))
                {
                    result[i].Id = "shape-" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            return result;
        }

        /// <summary>
        /// drawing XML から sp / cxnSp を抽出する（pic は無視）。
        /// </summary>
        private List<ShapeContent> ExtractShapesFromDrawing(string drawingPath, string sheetName)
        {
            var list = new List<ShapeContent>();
            XDocument doc = ReadXmlEntry(drawingPath);
            if (doc == null)
            {
                return list;
            }

            int order = 0;
            foreach (XElement anchor in doc.Descendants())
            {
                string local = anchor.Name.LocalName;
                bool isTwoCell = string.Equals(local, "twoCellAnchor", StringComparison.OrdinalIgnoreCase);
                bool isOneCell = string.Equals(local, "oneCellAnchor", StringComparison.OrdinalIgnoreCase);
                bool isAbsolute = string.Equals(local, "absoluteAnchor", StringComparison.OrdinalIgnoreCase);
                if (!isTwoCell && !isOneCell && !isAbsolute)
                {
                    continue;
                }

                // pic のみのアンカーは画像側で扱う
                bool hasPic = anchor.Elements().Any(e =>
                    string.Equals(e.Name.LocalName, "pic", StringComparison.OrdinalIgnoreCase));
                bool hasShape = anchor.Descendants().Any(e =>
                {
                    string ln = e.Name.LocalName;
                    return string.Equals(ln, "sp", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ln, "cxnSp", StringComparison.OrdinalIgnoreCase);
                });
                if (hasPic && !hasShape)
                {
                    continue;
                }

                int row0 = -1;
                int col0 = -1;
                int row1 = -1;
                int col1 = -1;
                XElement from = anchor.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "from", StringComparison.OrdinalIgnoreCase));
                if (from != null)
                {
                    TryReadMarkerRowCol(from, out row0, out col0);
                }

                if (isTwoCell)
                {
                    XElement to = anchor.Elements().FirstOrDefault(e =>
                        string.Equals(e.Name.LocalName, "to", StringComparison.OrdinalIgnoreCase));
                    if (to != null)
                    {
                        TryReadMarkerRowCol(to, out row1, out col1);
                    }
                }

                AnchorRect rect = isAbsolute
                    ? null
                    : AnchorRect.FromZeroBased(row0, col0, row1, col1);

                // 直接の sp/cxnSp および grpSp 内の sp/cxnSp
                foreach (XElement shapeEl in EnumerateShapeElements(anchor))
                {
                    ShapeContent sc = BuildShapeContent(shapeEl, rect, order, sheetName);
                    if (sc != null)
                    {
                        list.Add(sc);
                        order++;
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// アンカー配下の sp / cxnSp を列挙する（pic は除外。grpSp は再帰）。
        /// </summary>
        private static IEnumerable<XElement> EnumerateShapeElements(XElement anchor)
        {
            if (anchor == null)
            {
                yield break;
            }

            foreach (XElement child in anchor.Elements())
            {
                string ln = child.Name.LocalName;
                if (string.Equals(ln, "sp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ln, "cxnSp", StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                }
                else if (string.Equals(ln, "grpSp", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (XElement nested in EnumerateShapeElements(child))
                    {
                        yield return nested;
                    }
                }
            }
        }

        /// <summary>
        /// sp / cxnSp 要素から ShapeContent を構築する。
        /// </summary>
        private static ShapeContent BuildShapeContent(
            XElement shapeEl,
            AnchorRect rect,
            int order,
            string sheetName)
        {
            if (shapeEl == null)
            {
                return null;
            }

            string elementKind = shapeEl.Name.LocalName;
            string prst = null;
            XElement prstGeom = shapeEl.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "prstGeom", StringComparison.OrdinalIgnoreCase));
            if (prstGeom != null)
            {
                XAttribute prstAttr = prstGeom.Attribute("prst");
                if (prstAttr != null)
                {
                    prst = prstAttr.Value;
                }
            }

            string kind = !string.IsNullOrEmpty(prst)
                ? prst
                : (string.Equals(elementKind, "cxnSp", StringComparison.OrdinalIgnoreCase)
                    ? "connector"
                    : "shape");

            // 図形名（cNvPr@name）はメタ。Kind には幾何を優先
            string name = null;
            XElement cNvPr = shapeEl.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "cNvPr", StringComparison.OrdinalIgnoreCase));
            if (cNvPr != null)
            {
                XAttribute nameAttr = cNvPr.Attribute("name");
                if (nameAttr != null)
                {
                    name = nameAttr.Value;
                }
            }

            // 図形内テキスト（a:t 連結）
            var textParts = new List<string>();
            foreach (XElement t in shapeEl.Descendants())
            {
                if (string.Equals(t.Name.LocalName, "t", StringComparison.OrdinalIgnoreCase))
                {
                    string v = t.Value;
                    if (!string.IsNullOrEmpty(v))
                    {
                        textParts.Add(v);
                    }
                }
            }

            string text = textParts.Count > 0
                ? string.Join(string.Empty, textParts)
                : string.Empty;

            long cx = 0;
            long cy = 0;
            XElement ext = shapeEl.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase)
                && e.Parent != null
                && string.Equals(e.Parent.Name.LocalName, "xfrm", StringComparison.OrdinalIgnoreCase));
            if (ext != null)
            {
                long.TryParse((string)ext.Attribute("cx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cx);
                long.TryParse((string)ext.Attribute("cy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cy);
            }

            string fill = ExtractShapeFillKey(shapeEl);
            string contentHash = ComputeShapeContentHash(kind, text, cx, cy, fill, shapeEl);

            string idBase = !string.IsNullOrEmpty(name) ? name : kind;
            return new ShapeContent
            {
                Id = idBase + "@" + (sheetName ?? "?") + "#" + order.ToString(CultureInfo.InvariantCulture),
                OrderIndex = order,
                Kind = kind,
                Text = text,
                RasterPath = null,
                ContentHash = contentHash,
                Anchor = rect != null ? rect.Clone() : null
            };
        }

        /// <summary>
        /// 塗りつぶしの簡易キー（solidFill srgbClr など）。無ければ空。
        /// </summary>
        private static string ExtractShapeFillKey(XElement shapeEl)
        {
            if (shapeEl == null)
            {
                return string.Empty;
            }

            XElement solid = shapeEl.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "solidFill", StringComparison.OrdinalIgnoreCase));
            if (solid == null)
            {
                // noFill
                if (shapeEl.Descendants().Any(e =>
                    string.Equals(e.Name.LocalName, "noFill", StringComparison.OrdinalIgnoreCase)))
                {
                    return "nofill";
                }

                return string.Empty;
            }

            XElement srgb = solid.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase));
            if (srgb != null)
            {
                XAttribute val = srgb.Attribute("val");
                if (val != null)
                {
                    return "srgb:" + val.Value;
                }
            }

            XElement scheme = solid.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "schemeClr", StringComparison.OrdinalIgnoreCase));
            if (scheme != null)
            {
                XAttribute val = scheme.Attribute("val");
                if (val != null)
                {
                    return "scheme:" + val.Value;
                }
            }

            return "solid";
        }

        /// <summary>
        /// Text+Kind+サイズ+塗り、または XML 正規化指紋から ContentHash を計算する。
        /// </summary>
        private static string ComputeShapeContentHash(
            string kind,
            string text,
            long cx,
            long cy,
            string fill,
            XElement shapeEl)
        {
            string fingerprint;
            if (!string.IsNullOrEmpty(text))
            {
                // テキスト優先: Text + Kind + サイズ + 塗り
                fingerprint = string.Format(
                    CultureInfo.InvariantCulture,
                    "t|{0}|{1}|{2}x{3}|{4}",
                    text,
                    kind ?? string.Empty,
                    cx,
                    cy,
                    fill ?? string.Empty);
            }
            else
            {
                // テキストなし: Kind+サイズ+塗り + 正規化 XML 断片
                string xmlNorm = NormalizeShapeXml(shapeEl);
                fingerprint = string.Format(
                    CultureInfo.InvariantCulture,
                    "x|{0}|{1}x{2}|{3}|{4}",
                    kind ?? string.Empty,
                    cx,
                    cy,
                    fill ?? string.Empty,
                    xmlNorm);
            }

            return ComputeStringHash(fingerprint);
        }

        /// <summary>
        /// 図形 XML を id/name を除いた簡易正規化文字列にする。
        /// </summary>
        private static string NormalizeShapeXml(XElement shapeEl)
        {
            if (shapeEl == null)
            {
                return string.Empty;
            }

            try
            {
                XElement clone = new XElement(shapeEl);
                foreach (XElement el in clone.DescendantsAndSelf())
                {
                    // 不安定な識別子を落とす
                    el.Attribute("id")?.Remove();
                    el.Attribute("name")?.Remove();
                    // 名前空間接頭辞差を抑えるため LocalName ベースのタグ列に落とすのは重いので
                    // 属性整理後の Outer 風文字列を使う
                }

                // 空白正規化
                string raw = clone.ToString(SaveOptions.DisableFormatting);
                return Regex.Replace(raw ?? string.Empty, @"\s+", " ").Trim();
            }
            catch
            {
                return shapeEl.Name.LocalName;
            }
        }

        /// <summary>
        /// 文字列の SHA256 十六進。
        /// </summary>
        private static string ComputeStringHash(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_zip != null)
            {
                _zip.Dispose();
                _zip = null;
            }
        }

        /// <summary>
        /// workbook と rels からシート一覧を構築する。
        /// </summary>
        private void LoadWorkbookStructure()
        {
            _sheetPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sharedStrings = LoadSharedStrings();
            LoadStyles();

            XDocument workbook = ReadXmlEntry("xl/workbook.xml");
            if (workbook == null)
            {
                throw new InvalidOperationException("xl/workbook.xml がありません: " + _path);
            }

            Dictionary<string, string> rels = LoadRelationships("xl/_rels/workbook.xml.rels");
            foreach (XElement sheet in workbook.Descendants(NsMain + "sheet"))
            {
                string name = (string)sheet.Attribute("name");
                string rid = (string)sheet.Attribute(NsOfficeRel + "id");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(rid))
                {
                    continue;
                }

                string target;
                if (!rels.TryGetValue(rid, out target))
                {
                    continue;
                }

                // Target 例: "/xl/worksheets/sheet1.xml" または "worksheets/sheet1.xml"
                // 誤って "xl/" + "xl/worksheets/..." にしない
                string sheetPath = ResolveWorkbookPartPath(target);
                if (!_sheetPaths.ContainsKey(name))
                {
                    _sheetPaths.Add(name, sheetPath);
                }
            }

            if (_sheetPaths.Count == 0)
            {
                throw new InvalidOperationException("シートが見つかりません: " + _path);
            }
        }

        /// <summary>
        /// xl/styles.xml（と任意で theme）から fill / border を cellXfs 単位で解決する。
        /// </summary>
        private void LoadStyles()
        {
            // 最低限のデフォルト（style index 0 = 標準）
            _xfBackgroundArgb = new string[] { null };
            _xfHasAnyBorder = new bool[] { false };
            _themeColors = CreateDefaultThemeColors();
            _indexedColors = CreateDefaultIndexedColors();

            try
            {
                LoadThemeColors();
            }
            catch (Exception ex)
            {
                Log.Debug("theme 色読込スキップ: " + ex.Message);
            }

            XDocument doc = ReadXmlEntry("xl/styles.xml");
            if (doc == null)
            {
                return;
            }

            try
            {
                // カスタム indexedColors（あれば上書き）
                XElement colorsRoot = doc.Root != null ? doc.Root.Element(NsMain + "colors") : null;
                if (colorsRoot != null)
                {
                    XElement indexed = colorsRoot.Element(NsMain + "indexedColors");
                    if (indexed != null)
                    {
                        var list = new List<string>();
                        foreach (XElement rgbColor in indexed.Elements(NsMain + "rgbColor"))
                        {
                            list.Add(NormalizeArgb((string)rgbColor.Attribute("rgb")));
                        }

                        if (list.Count > 0)
                        {
                            // 既存パレット長を維持しつつ先頭を差し替え
                            string[] merged = CreateDefaultIndexedColors();
                            for (int i = 0; i < list.Count && i < merged.Length; i++)
                            {
                                if (list[i] != null)
                                {
                                    merged[i] = list[i];
                                }
                            }

                            _indexedColors = merged;
                        }
                    }
                }

                // fills: インデックス → 背景 ARGB
                var fillArgb = new List<string>();
                XElement fills = doc.Root.Element(NsMain + "fills");
                if (fills != null)
                {
                    foreach (XElement fill in fills.Elements(NsMain + "fill"))
                    {
                        fillArgb.Add(ResolveFillBackground(fill));
                    }
                }

                if (fillArgb.Count == 0)
                {
                    fillArgb.Add(null);
                }

                // borders: インデックス → 四辺いずれか有り
                var borderFlags = new List<bool>();
                XElement borders = doc.Root.Element(NsMain + "borders");
                if (borders != null)
                {
                    foreach (XElement border in borders.Elements(NsMain + "border"))
                    {
                        borderFlags.Add(BorderHasAnySide(border));
                    }
                }

                if (borderFlags.Count == 0)
                {
                    borderFlags.Add(false);
                }

                // cellXfs
                XElement cellXfs = doc.Root.Element(NsMain + "cellXfs");
                if (cellXfs == null)
                {
                    return;
                }

                var bgByXf = new List<string>();
                var borderByXf = new List<bool>();
                foreach (XElement xf in cellXfs.Elements(NsMain + "xf"))
                {
                    int fillId = ParseIntAttr(xf, "fillId", 0);
                    int borderId = ParseIntAttr(xf, "borderId", 0);

                    string bg = null;
                    if (fillId >= 0 && fillId < fillArgb.Count)
                    {
                        bg = fillArgb[fillId];
                    }

                    bool hasBorder = false;
                    if (borderId >= 0 && borderId < borderFlags.Count)
                    {
                        hasBorder = borderFlags[borderId];
                    }

                    bgByXf.Add(bg);
                    borderByXf.Add(hasBorder);
                }

                if (bgByXf.Count > 0)
                {
                    _xfBackgroundArgb = bgByXf.ToArray();
                    _xfHasAnyBorder = borderByXf.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("styles.xml 解析失敗: " + ex.Message);
            }
        }

        /// <summary>
        /// fill 要素から solid 背景 ARGB を得る。塗りなし・解決不能は null。
        /// </summary>
        private string ResolveFillBackground(XElement fill)
        {
            if (fill == null)
            {
                return null;
            }

            // patternFill を優先。gradientFill は簡易対応しない
            XElement pattern = fill.Element(NsMain + "patternFill");
            if (pattern == null)
            {
                return null;
            }

            string patternType = (string)pattern.Attribute("patternType");
            // 未指定・none・Excel 既定の gray125/gray0625 は「塗りなし」扱い
            if (string.IsNullOrEmpty(patternType)
                || string.Equals(patternType, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(patternType, "gray125", StringComparison.OrdinalIgnoreCase)
                || string.Equals(patternType, "gray0625", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // solid は fgColor。その他パターンも fg があれば採用（簡易）
            XElement fg = pattern.Element(NsMain + "fgColor");
            string argb = ResolveColorElement(fg);
            if (argb != null)
            {
                return argb;
            }

            XElement bg = pattern.Element(NsMain + "bgColor");
            return ResolveColorElement(bg);
        }

        /// <summary>
        /// color 要素（rgb / theme / indexed）を #AARRGGBB に解決する。
        /// </summary>
        private string ResolveColorElement(XElement colorEl)
        {
            if (colorEl == null)
            {
                return null;
            }

            string rgb = (string)colorEl.Attribute("rgb");
            if (!string.IsNullOrEmpty(rgb))
            {
                return NormalizeArgb(rgb);
            }

            string themeStr = (string)colorEl.Attribute("theme");
            if (!string.IsNullOrEmpty(themeStr))
            {
                int themeIdx;
                if (int.TryParse(themeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out themeIdx)
                    && _themeColors != null
                    && themeIdx >= 0
                    && themeIdx < _themeColors.Length
                    && _themeColors[themeIdx] != null)
                {
                    string baseColor = _themeColors[themeIdx];
                    string tintStr = (string)colorEl.Attribute("tint");
                    double tint;
                    if (!string.IsNullOrEmpty(tintStr)
                        && double.TryParse(tintStr, NumberStyles.Float, CultureInfo.InvariantCulture, out tint)
                        && Math.Abs(tint) > 1e-9)
                    {
                        return ApplyTint(baseColor, tint);
                    }

                    return baseColor;
                }
            }

            string indexedStr = (string)colorEl.Attribute("indexed");
            if (!string.IsNullOrEmpty(indexedStr))
            {
                int idx;
                if (int.TryParse(indexedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                    && _indexedColors != null
                    && idx >= 0
                    && idx < _indexedColors.Length)
                {
                    return _indexedColors[idx];
                }
            }

            return null;
        }

        /// <summary>
        /// ボーダー四辺のいずれかが style != none なら true。
        /// </summary>
        private static bool BorderHasAnySide(XElement border)
        {
            if (border == null)
            {
                return false;
            }

            string[] sides = { "left", "right", "top", "bottom" };
            foreach (string side in sides)
            {
                XElement el = border.Element(NsMain + side);
                if (el == null)
                {
                    continue;
                }

                string style = (string)el.Attribute("style");
                if (!string.IsNullOrEmpty(style)
                    && !string.Equals(style, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// OOXML の色文字列を #AARRGGBB に正規化する。
        /// 6 桁は不透明扱い。8 桁で alpha=00 は Excel 慣習上不透明として FF にする。
        /// </summary>
        private static string NormalizeArgb(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string s = raw.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
            }

            s = s.ToUpperInvariant();
            if (s.Length == 6)
            {
                return "#FF" + s;
            }

            if (s.Length == 8)
            {
                // openpyxl 等は alpha を 00 で書き、Excel は不透明として扱う
                if (s.StartsWith("00", StringComparison.Ordinal))
                {
                    return "#FF" + s.Substring(2);
                }

                return "#" + s;
            }

            return null;
        }

        /// <summary>
        /// theme 色に tint を適用する（ECMA-376 簡易実装）。
        /// </summary>
        private static string ApplyTint(string argb, double tint)
        {
            if (string.IsNullOrEmpty(argb) || argb.Length < 9)
            {
                return argb;
            }

            int a = ParseHexByte(argb, 1);
            int r = ParseHexByte(argb, 3);
            int g = ParseHexByte(argb, 5);
            int b = ParseHexByte(argb, 7);

            if (tint < 0)
            {
                r = (int)Math.Round(r * (1.0 + tint));
                g = (int)Math.Round(g * (1.0 + tint));
                b = (int)Math.Round(b * (1.0 + tint));
            }
            else
            {
                r = (int)Math.Round(r * (1.0 - tint) + 255.0 * tint);
                g = (int)Math.Round(g * (1.0 - tint) + 255.0 * tint);
                b = (int)Math.Round(b * (1.0 - tint) + 255.0 * tint);
            }

            r = ClampByte(r);
            g = ClampByte(g);
            b = ClampByte(b);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", a, r, g, b);
        }

        private static int ParseHexByte(string argb, int start)
        {
            int v;
            if (int.TryParse(argb.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }

            return 0;
        }

        private static int ClampByte(int v)
        {
            if (v < 0)
            {
                return 0;
            }

            if (v > 255)
            {
                return 255;
            }

            return v;
        }

        private static int ParseIntAttr(XElement el, string name, int defaultValue)
        {
            if (el == null)
            {
                return defaultValue;
            }

            string v = (string)el.Attribute(name);
            int n;
            if (!string.IsNullOrEmpty(v)
                && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            {
                return n;
            }

            return defaultValue;
        }

        /// <summary>
        /// xl/theme/theme1.xml から clrScheme を読む（無ければ既定のまま）。
        /// </summary>
        private void LoadThemeColors()
        {
            // theme パスは固定または rels から。まず定番パスを試す
            string[] candidates =
            {
                "xl/theme/theme1.xml",
                "xl/theme/theme.xml"
            };

            XDocument themeDoc = null;
            foreach (string path in candidates)
            {
                themeDoc = ReadXmlEntry(path);
                if (themeDoc != null)
                {
                    break;
                }
            }

            if (themeDoc == null)
            {
                return;
            }

            // a:clrScheme 内の順: dk1, lt1, dk2, lt2, accent1..6, hlink, folHlink
            // SpreadsheetML theme インデックス: 0=dk1, 1=lt1, ...
            XElement clrScheme = themeDoc.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "clrScheme", StringComparison.OrdinalIgnoreCase));
            if (clrScheme == null)
            {
                return;
            }

            string[] order =
            {
                "dk1", "lt1", "dk2", "lt2",
                "accent1", "accent2", "accent3", "accent4", "accent5", "accent6",
                "hlink", "folHlink"
            };

            var colors = CreateDefaultThemeColors();
            for (int i = 0; i < order.Length; i++)
            {
                string local = order[i];
                XElement slot = clrScheme.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase));
                if (slot == null)
                {
                    continue;
                }

                string resolved = ResolveThemeSlotColor(slot);
                if (resolved != null)
                {
                    colors[i] = resolved;
                }
            }

            _themeColors = colors;
        }

        /// <summary>
        /// theme の 1 スロット（sysClr / srgbClr）から ARGB を得る。
        /// </summary>
        private static string ResolveThemeSlotColor(XElement slot)
        {
            if (slot == null)
            {
                return null;
            }

            foreach (XElement child in slot.Elements())
            {
                string local = child.Name.LocalName;
                if (string.Equals(local, "srgbClr", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeArgb((string)child.Attribute("val"));
                }

                if (string.Equals(local, "sysClr", StringComparison.OrdinalIgnoreCase))
                {
                    string last = (string)child.Attribute("lastClr");
                    if (!string.IsNullOrEmpty(last))
                    {
                        return NormalizeArgb(last);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Office 既定に近い theme 色（#AARRGGBB）。
        /// </summary>
        private static string[] CreateDefaultThemeColors()
        {
            return new[]
            {
                "#FF000000", // 0 dk1
                "#FFFFFFFF", // 1 lt1
                "#FF1F497D", // 2 dk2
                "#FFEEECE1", // 3 lt2
                "#FF4F81BD", // 4 accent1
                "#FFC0504D", // 5 accent2
                "#FF9BBB59", // 6 accent3
                "#FF8064A2", // 7 accent4
                "#FF4BACC6", // 8 accent5
                "#FFF79646", // 9 accent6
                "#FF0000FF", // 10 hlink
                "#FF800080"  // 11 folHlink
            };
        }

        /// <summary>
        /// ECMA-376 既定 indexed パレット（先頭 64 + システム予約を簡易化）。
        /// </summary>
        private static string[] CreateDefaultIndexedColors()
        {
            // 主要 64 色 + 65/66 用の黒白（システム）を含む簡易表
            return new[]
            {
                "#FF000000", "#FFFFFFFF", "#FFFF0000", "#FF00FF00", "#FF0000FF", "#FFFFFF00", "#FFFF00FF", "#FF00FFFF",
                "#FF000000", "#FFFFFFFF", "#FFFF0000", "#FF00FF00", "#FF0000FF", "#FFFFFF00", "#FFFF00FF", "#FF00FFFF",
                "#FF800000", "#FF008000", "#FF000080", "#FF808000", "#FF800080", "#FF008080", "#FFC0C0C0", "#FF808080",
                "#FF9999FF", "#FF993366", "#FFFFFFCC", "#FFCCFFFF", "#FF660066", "#FFFF8080", "#FF0066CC", "#FFCCCCFF",
                "#FF000080", "#FFFF00FF", "#FFFFFF00", "#FF00FFFF", "#FF800080", "#FF800000", "#FF008080", "#FF0000FF",
                "#FF00CCFF", "#FFCCFFFF", "#FFCCFFCC", "#FFFFFF99", "#FF99CCFF", "#FFFF99CC", "#FFCC99FF", "#FFFFCC99",
                "#FF3366FF", "#FF33CCCC", "#FF99CC00", "#FFFFCC00", "#FFFF9900", "#FFFF6600", "#FF666699", "#FF969696",
                "#FF003366", "#FF339966", "#FF003300", "#FF333300", "#FF993300", "#FF993366", "#FF333399", "#FF333333",
                "#FF000000", // 64 system
                "#FFFFFFFF"  // 65 system
            };
        }

        /// <summary>
        /// 共有文字列を読み込む。
        /// </summary>
        private List<string> LoadSharedStrings()
        {
            var list = new List<string>();
            XDocument doc = ReadXmlEntry("xl/sharedStrings.xml");
            if (doc == null)
            {
                return list;
            }

            foreach (XElement si in doc.Descendants(NsMain + "si"))
            {
                // リッチテキストは t を連結
                var parts = si.Descendants(NsMain + "t").Select(t => t.Value);
                list.Add(string.Concat(parts));
            }

            return list;
        }

        /// <summary>
        /// セル要素から表示テキストを得る。
        /// </summary>
        private string ReadCellText(XElement c)
        {
            string type = (string)c.Attribute("t");
            XElement v = c.Element(NsMain + "v");
            XElement isElem = c.Element(NsMain + "is");

            if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase) && isElem != null)
            {
                return string.Concat(isElem.Descendants(NsMain + "t").Select(t => t.Value));
            }

            if (v == null)
            {
                return string.Empty;
            }

            string raw = v.Value ?? string.Empty;
            if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase))
            {
                int index;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    && index >= 0 && index < _sharedStrings.Count)
                {
                    return _sharedStrings[index];
                }

                return raw;
            }

            // 数式セルもキャッシュ値 v を返す。b (bool) 等もそのまま
            return raw;
        }

        /// <summary>
        /// relationships を Id → Target で読む。
        /// </summary>
        private Dictionary<string, string> LoadRelationships(string relsPath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            XDocument doc = ReadXmlEntry(relsPath);
            if (doc == null)
            {
                return map;
            }

            foreach (XElement rel in doc.Descendants(NsRel + "Relationship"))
            {
                string id = (string)rel.Attribute("Id");
                string target = (string)rel.Attribute("Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    map[id] = target.Replace('\\', '/');
                }
            }

            return map;
        }

        /// <summary>
        /// drawing 上の media とアンカー位置（from〜to、1 始まり inclusive）。
        /// </summary>
        private sealed class DrawingMediaAnchor
        {
            public string MediaPackagePath;
            public int Row1Based;
            public int Column1Based;
            public int RowEnd1Based;
            public int ColumnEnd1Based;
        }

        /// <summary>
        /// シート → drawing media（行位置付き）のマップを構築する。
        /// </summary>
        private Dictionary<string, List<DrawingMediaAnchor>> TryMapSheetMediaAnchors()
        {
            var result = new Dictionary<string, List<DrawingMediaAnchor>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var kv in _sheetPaths)
                {
                    string sheetName = kv.Key;
                    string sheetPath = kv.Value;
                    string sheetDir = GetPackageDirectory(sheetPath);
                    string sheetFile = Path.GetFileName(sheetPath);
                    string relsPath = sheetDir + "/_rels/" + sheetFile + ".rels";
                    Dictionary<string, string> sheetRels = LoadRelationships(relsPath);

                    foreach (var rel in sheetRels)
                    {
                        if (rel.Value.IndexOf("drawing", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        string drawingPath = ResolveRelativePackagePath(sheetDir, rel.Value);
                        List<DrawingMediaAnchor> anchors = ExtractAnchorsFromDrawing(drawingPath);
                        if (anchors.Count == 0)
                        {
                            continue;
                        }

                        List<DrawingMediaAnchor> list;
                        if (!result.TryGetValue(sheetName, out list))
                        {
                            list = new List<DrawingMediaAnchor>();
                            result[sheetName] = list;
                        }

                        list.AddRange(anchors);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryMapSheetMediaAnchors 失敗: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// drawing XML と rels から media パスとアンカー行を集める。
        /// </summary>
        private List<DrawingMediaAnchor> ExtractAnchorsFromDrawing(string drawingPath)
        {
            var list = new List<DrawingMediaAnchor>();
            string drawingDir = GetPackageDirectory(drawingPath);
            string drawingFile = Path.GetFileName(drawingPath);
            string relsPath = drawingDir + "/_rels/" + drawingFile + ".rels";
            Dictionary<string, string> rels = LoadRelationships(relsPath);

            XDocument doc = ReadXmlEntry(drawingPath);
            if (doc == null)
            {
                // フォールバック: 行不明で media のみ
                foreach (string target in rels.Values)
                {
                    if (target.IndexOf("media", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string mediaPath = ResolveRelativePackagePath(drawingDir, target);
                    list.Add(new DrawingMediaAnchor
                    {
                        MediaPackagePath = NormalizePackagePath(mediaPath),
                        Row1Based = 0,
                        Column1Based = 0
                    });
                }

                return list;
            }

            foreach (XElement anchor in doc.Descendants())
            {
                string local = anchor.Name.LocalName;
                bool isTwoCell = string.Equals(local, "twoCellAnchor", StringComparison.OrdinalIgnoreCase);
                bool isOneCell = string.Equals(local, "oneCellAnchor", StringComparison.OrdinalIgnoreCase);
                bool isAbsolute = string.Equals(local, "absoluteAnchor", StringComparison.OrdinalIgnoreCase);
                if (!isTwoCell && !isOneCell && !isAbsolute)
                {
                    continue;
                }

                int row0 = -1;
                int col0 = -1;
                int row1 = -1;
                int col1 = -1;
                XElement from = anchor.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "from", StringComparison.OrdinalIgnoreCase));
                if (from != null)
                {
                    TryReadMarkerRowCol(from, out row0, out col0);
                }

                // twoCellAnchor のみ to を読む。oneCell は Start=End。absolute は行 0 のまま（マップ非対象）
                if (isTwoCell)
                {
                    XElement to = anchor.Elements().FirstOrDefault(e =>
                        string.Equals(e.Name.LocalName, "to", StringComparison.OrdinalIgnoreCase));
                    if (to != null)
                    {
                        TryReadMarkerRowCol(to, out row1, out col1);
                    }
                }

                // blip r:embed
                string embedId = null;
                foreach (XElement blip in anchor.Descendants())
                {
                    if (!string.Equals(blip.Name.LocalName, "blip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    XAttribute embed = blip.Attribute(NsR + "embed")
                        ?? blip.Attributes().FirstOrDefault(a =>
                            string.Equals(a.Name.LocalName, "embed", StringComparison.OrdinalIgnoreCase));
                    if (embed != null && !string.IsNullOrEmpty(embed.Value))
                    {
                        embedId = embed.Value;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(embedId))
                {
                    continue;
                }

                string target;
                if (!rels.TryGetValue(embedId, out target)
                    || target.IndexOf("media", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string mediaPath = ResolveRelativePackagePath(drawingDir, target);
                // absoluteAnchor はセル座標なし → 0 のまま（マップ非対象）
                // oneCell: to 無し → Start=End。twoCell: to を inclusive 正規化
                AnchorRect rect = isAbsolute
                    ? null
                    : AnchorRect.FromZeroBased(row0, col0, row1, col1);
                list.Add(new DrawingMediaAnchor
                {
                    MediaPackagePath = NormalizePackagePath(mediaPath),
                    Row1Based = rect != null ? rect.RowStart : 0,
                    Column1Based = rect != null ? rect.ColStart : 0,
                    RowEnd1Based = rect != null ? rect.RowEnd : 0,
                    ColumnEnd1Based = rect != null ? rect.ColEnd : 0
                });
            }

            // パース失敗時のフォールバック
            if (list.Count == 0)
            {
                foreach (string target in rels.Values)
                {
                    if (target.IndexOf("media", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string mediaPath = ResolveRelativePackagePath(drawingDir, target);
                    list.Add(new DrawingMediaAnchor
                    {
                        MediaPackagePath = NormalizePackagePath(mediaPath),
                        Row1Based = 0,
                        Column1Based = 0
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// drawing の from/to マーカーから 0-based row/col を読む。
        /// </summary>
        private static void TryReadMarkerRowCol(XElement marker, out int row0, out int col0)
        {
            row0 = -1;
            col0 = -1;
            if (marker == null)
            {
                return;
            }

            XElement rowEl = marker.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "row", StringComparison.OrdinalIgnoreCase));
            XElement colEl = marker.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "col", StringComparison.OrdinalIgnoreCase));
            if (rowEl != null)
            {
                int.TryParse(rowEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out row0);
            }

            if (colEl != null)
            {
                int.TryParse(colEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out col0);
            }
        }

        /// <summary>
        /// ZIP エントリを XML として読む。
        /// </summary>
        private XDocument ReadXmlEntry(string entryPath)
        {
            ZipArchiveEntry entry = FindEntry(entryPath);
            if (entry == null)
            {
                return null;
            }

            using (Stream stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return XDocument.Load(reader);
            }
        }

        /// <summary>
        /// エントリを大小無視で探す。
        /// </summary>
        private ZipArchiveEntry FindEntry(string entryPath)
        {
            string norm = NormalizePackagePath(entryPath);
            return _zip.Entries.FirstOrDefault(e =>
                string.Equals(NormalizePackagePath(e.FullName), norm, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// パッケージパスを正規化する。
        /// </summary>
        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// workbook.xml.rels の Target を ZIP 内パスへ解決する。
        /// </summary>
        private static string ResolveWorkbookPartPath(string target)
        {
            string t = NormalizePackagePath(target);
            if (string.IsNullOrEmpty(t))
            {
                return t;
            }

            // 既に xl/ で始まる（絶対パッケージパスの Trim 後）
            if (t.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }

            // 相対: worksheets/sheet1.xml
            return "xl/" + t;
        }

        /// <summary>
        /// パッケージ上のディレクトリ部分。
        /// </summary>
        private static string GetPackageDirectory(string packagePath)
        {
            string norm = NormalizePackagePath(packagePath);
            int idx = norm.LastIndexOf('/');
            return idx >= 0 ? norm.Substring(0, idx) : string.Empty;
        }

        /// <summary>
        /// 相対 Target をパッケージ絶対パスへ。
        /// </summary>
        private static string ResolveRelativePackagePath(string baseDir, string target)
        {
            target = (target ?? string.Empty).Replace('\\', '/');
            if (target.StartsWith("/"))
            {
                return NormalizePackagePath(target);
            }

            // ../ を簡易解決
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(baseDir))
            {
                parts.AddRange(baseDir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            }

            foreach (string seg in target.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg == ".")
                {
                    continue;
                }

                if (seg == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }

                    continue;
                }

                parts.Add(seg);
            }

            return string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// A1 アドレスを行列に分解する。
        /// </summary>
        public static void ParseAddress(string address, out int row, out int column)
        {
            row = 0;
            column = 0;
            if (string.IsNullOrEmpty(address))
            {
                return;
            }

            Match m = CellRefRegex.Match(address);
            if (!m.Success)
            {
                return;
            }

            column = ColumnLettersToIndex(m.Groups[1].Value);
            int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out row);
        }

        /// <summary>
        /// 列文字を 1 始まりインデックスへ。
        /// </summary>
        private static int ColumnLettersToIndex(string letters)
        {
            int col = 0;
            foreach (char ch in letters.ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z')
                {
                    continue;
                }

                col = col * 26 + (ch - 'A' + 1);
            }

            return col;
        }

        /// <summary>
        /// ファイルの SHA256 を十六進で返す。
        /// </summary>
        private static string ComputeFileHash(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// PNG/JPEG ヘッダから幅・高さを読む（画像全体はデコードしない）。
        /// </summary>
        private static bool TryReadImageSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 24)
                    {
                        return false;
                    }

                    var header = new byte[24];
                    int read = fs.Read(header, 0, header.Length);
                    if (read < 24)
                    {
                        return false;
                    }

                    // PNG: 89 50 4E 47 ... IHDR at offset 12, width/height BE at 16/20
                    if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    {
                        width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                        height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                        return width > 0 && height > 0;
                    }

                    // JPEG: FF D8 ... find SOF0/2
                    if (header[0] == 0xFF && header[1] == 0xD8)
                    {
                        fs.Position = 2;
                        while (fs.Position + 9 < fs.Length)
                        {
                            int b0 = fs.ReadByte();
                            if (b0 < 0)
                            {
                                break;
                            }

                            if (b0 != 0xFF)
                            {
                                continue;
                            }

                            int marker = fs.ReadByte();
                            while (marker == 0xFF)
                            {
                                marker = fs.ReadByte();
                            }

                            if (marker < 0)
                            {
                                break;
                            }

                            // SOF0..SOF3, SOF5..SOF7, SOF9..SOF11, SOF13..SOF15 (not DHT/DAC etc.)
                            bool isSof = (marker >= 0xC0 && marker <= 0xC3)
                                || (marker >= 0xC5 && marker <= 0xC7)
                                || (marker >= 0xC9 && marker <= 0xCB)
                                || (marker >= 0xCD && marker <= 0xCF);
                            if (marker == 0xD8 || marker == 0xD9)
                            {
                                continue;
                            }

                            int lenHi = fs.ReadByte();
                            int lenLo = fs.ReadByte();
                            if (lenHi < 0 || lenLo < 0)
                            {
                                break;
                            }

                            int segLen = (lenHi << 8) | lenLo;
                            if (segLen < 2)
                            {
                                break;
                            }

                            if (isSof)
                            {
                                int precision = fs.ReadByte();
                                int hHi = fs.ReadByte();
                                int hLo = fs.ReadByte();
                                int wHi = fs.ReadByte();
                                int wLo = fs.ReadByte();
                                if (precision < 0 || hHi < 0 || hLo < 0 || wHi < 0 || wLo < 0)
                                {
                                    return false;
                                }

                                height = (hHi << 8) | hLo;
                                width = (wHi << 8) | wLo;
                                return width > 0 && height > 0;
                            }

                            fs.Position += segLen - 2;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryReadImageSize: " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// オープン状態チェック。
        /// </summary>
        private void EnsureOpen()
        {
            if (_disposed || _zip == null)
            {
                throw new ObjectDisposedException(nameof(XlsxPackageReader));
            }
        }
    }
}
