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
        /// 指定シートのセルを列挙する。
        /// </summary>
        /// <param name="sheetName">シート名</param>
        /// <returns>セル値</returns>
        public IEnumerable<CellValue> EnumerateCells(string sheetName)
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
                yield return new CellValue
                {
                    Address = address,
                    Text = text ?? string.Empty,
                    Row = row,
                    Column = col
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
