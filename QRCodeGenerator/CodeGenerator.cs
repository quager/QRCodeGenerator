using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QRCodeGenerator
{
    public enum Level
    {
        L = 0, // 7%  loss
        M = 1, // 15% loss
        Q = 2, // 25% loss
        H = 3  // 30% loss
    }

    public enum EncodingMode
    {
        Numeric,
        AlphaNumeric,
        Bytes,
        Kanji
    }

    public class Palette
    {
        public static Color DefaultBackgroundColor = Colors.White;
        public static Color DefaultDataColor = Colors.Black;

        private Color _backgroundColor = DefaultBackgroundColor;
        private Color _dataColor = DefaultDataColor;
        private Color? _alignmentPatternColor;
        private Color? _searchPatternColor;
        private Color? _syncBandColor;
        private Color? _versionColor;
        private Color? _moduleColor;
        private Color? _formatColor;

        public bool Inverted { get; set; }

        public Color MarkColor { get; set; } = Colors.Red;

        public Color BackgroundColor
        {
            get => Inverted ? _dataColor : _backgroundColor;
            set => _backgroundColor = value;
        }

        public Color DataColor
        {
            get => Inverted ? _backgroundColor : _dataColor;
            set => _dataColor = value;
        }

        public Color AlignmentPatternColor
        {
            get => _alignmentPatternColor ?? DataColor;
            set => _alignmentPatternColor = value;
        }

        public Color SearchPatternColor
        {
            get => _searchPatternColor ?? DataColor;
            set => _searchPatternColor = value;
        }

        public Color SyncBandColor
        {
            get => _syncBandColor ?? DataColor;
            set => _syncBandColor = value;
        }

        public Color VersionColor
        {
            get => _versionColor ?? DataColor;
            set => _versionColor = value;
        }

        public Color ModuleColor
        {
            get => _moduleColor ?? DataColor;
            set => _moduleColor = value;
        }

        public Color FormatColor
        {
            get => _formatColor ?? DataColor;
            set => _formatColor = value;
        }
    }

    public class CodeGenerator
    {
        private const int FreeSpacePoint = 8;
        private const int SyncBandPosition = 6;
        private const int VersionYPosition = 11;
        private const int SearchPatternSize = 7;
        private const int DefaultModuleSize = 4;
        private const int DefaultCodeSize = 500;
        private const Level DefaultCorrectionLevel = Level.M;

        public event Action OnImageUpdated;

        public int Mask { get; set; } = -1;

        public int Delay { get; set; } = 10;

        public int Version { get; set; } = 0;

        public int FreeSpace { get; set; } = 2;

        public int Size => (Version - 1) * CodeInfo.DeltaWidth + CodeInfo.StartWidth;

        public bool MaskOnly { get; set; }

        public bool UpdateImageOnDraw { get; set; }

        public BitmapSource Image { get; private set; }

        public Palette CodePalette { get; } = new Palette();

        public int CodeSize { get; set; } = DefaultCodeSize;

        public Level CorrectionLevel { get; set; } = DefaultCorrectionLevel;

        public BitmapSource Generate(string data, out int usedVersion, out int usedMask)
        {
            usedVersion = 0;
            usedMask = -1;

            if (string.IsNullOrEmpty(data))
                return null;

            Logger.WriteLog($"Generate input: {data}");
            List<List<byte>> dataBlocks;

            if (Version > 0)
                dataBlocks = DataAnalyzer.GetEncodedData(data, CorrectionLevel, Version, out usedVersion);
            else
                dataBlocks = DataAnalyzer.GetEncodedData(data, CorrectionLevel, out usedVersion);

            Version = usedVersion;

            List<List<byte>> correctionBlocks = GenerateCorrectionBlocks(dataBlocks);
            List<byte> dataStream = WriteDataStream(dataBlocks, correctionBlocks);
            bool[] dataToDraw = CreateDataToDraw(dataStream);
            Draw(dataToDraw, out usedMask);

            return Image;
        }

        private Color?[,] DrawMask(Color?[,] qr, int mask)
        {
            Color?[,] filled = (Color?[,])qr.Clone();
            Func<int, int, bool> maskFunction = CodeInfo.Masks[mask];

            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++)
                    filled[i, j] = maskFunction(i, j) ? CodePalette.ModuleColor : CodePalette.BackgroundColor;
            }

            DoUpdateImageOnDraw(filled);

            return filled;
        }

        private List<byte> WriteDataStream(List<List<byte>> blocks, List<List<byte>> correctionBlocks)
        {
            List<byte> dataStream = new List<byte>();

            WriteBlocksToStream(blocks, dataStream);
            WriteBlocksToStream(correctionBlocks, dataStream);

            Logger.WriteLog($"DataStream to write: [{string.Join(" ", dataStream.Select(d => Logger.ToHexString(d)))}]");

            return dataStream;
        }

        private void WriteBlocksToStream(List<List<byte>> blocks, List<byte> dataStream)
        {
            int i = 0;
            int n = 0;

            while (true)
            {
                if (n >= blocks[i].Count)
                {
                    i++;

                    if (i >= blocks.Count)
                        break;

                    continue;
                }

                dataStream.Add(blocks[i][n]);
                i++;

                if (i >= blocks.Count)
                {
                    i = 0;
                    n++;
                }
            }
        }

        private bool[] CreateDataToDraw(List<byte> dataStream)
        {
            StringBuilder datastring = new StringBuilder();
            List<bool> result = new List<bool>();
            foreach (byte value in dataStream)
            {
                string str = Convert.ToString(value, 2).PadLeft(8, '0');
                foreach (char c in str)
                {
                    datastring.Append(c);
                    result.Add(c == '1');
                }
            }

            Logger.WriteLog($"DataStream binary: {datastring}");
            return result.ToArray();
        }

        private List<List<byte>> GenerateCorrectionBlocks(List<List<byte>> blocks)
        {
            int nCorrWords = CodeInfo.NumberOfCorrectionWords[(int)CorrectionLevel, Version - 1];
            List<byte> gPoly = CodeInfo.GeneratingPolynomial[nCorrWords];
            List<List<byte>> result = new List<List<byte>>();

            for (int i = 0; i < blocks.Count; i++)
            {
                List<byte> block = blocks[i];
                int dataCount = block.Count;

                for (int step = 0; step < dataCount; step++)
                {
                    List<byte> correctionBytes = new List<byte>();
                    int max = Math.Max(block.Count, nCorrWords + 1);
                    int m = CodeInfo.IndexLookupTable[block[0]];

                    for (int n = 0; n < max; n++)
                    {
                        if (n > nCorrWords)
                        {
                            correctionBytes.Add(0);
                            continue;
                        }

                        int index = (m + gPoly[n]) % 255;
                        byte val = CodeInfo.AlphaLookupTable[index];
                        correctionBytes.Add(val);
                    }

                    for (int n = 0; n < max; n++)
                    {
                        byte val = n >= block.Count ? (byte)0 : block[n];
                        correctionBytes[n] ^= val;
                    }

                    while (correctionBytes[0] == 0)
                        correctionBytes.RemoveAt(0);

                    block = correctionBytes;
                }

                result.Add(block);
            }

            Logger.WriteLog($"GenerateCorrectionBlocks result:\r\n    " +
                string.Join("\r\n    ", result.Select((x, i) => $"Block {i}. [{string.Join(" ", x.Select(d => Logger.ToHexString(d)))}]")));

            return result;
        }

        private void Draw(bool[] dataStream, out int usedMask)
        {
            Color?[,] qr = new Color?[Size, Size];

            FillPatterns(qr);
            DoUpdateImageOnDraw(qr);

            DrawSearchPatterns(qr);
            DoUpdateImageOnDraw(qr);

            DrawSyncBands(qr);
            DoUpdateImageOnDraw(qr);

            DrawAlignmentPatterns(qr);
            DoUpdateImageOnDraw(qr);

            DrawVersion(qr);
            DoUpdateImageOnDraw(qr);

            Color?[,] theBestQr = DrawData(dataStream, qr, out usedMask);

            if (MaskOnly)
                theBestQr = DrawMask(qr, usedMask);

            if (!UpdateImageOnDraw)
                Image = Scaling(theBestQr);

            DoUpdateImageOnDraw(theBestQr);
        }

        private void FillPatterns(Color?[,] qr)
        {
            for (int y = 0; y <= FreeSpacePoint; y++)
            {
                for (int x = 0; x <= FreeSpacePoint; x++)
                {
                    qr[x, y] = CodePalette.BackgroundColor;

                    if (x < FreeSpacePoint)
                        qr[Size - x - 1, y] = CodePalette.BackgroundColor;

                    if (y < FreeSpacePoint)
                        qr[x, Size - y - 1] = CodePalette.BackgroundColor;
                }
            }
        }

        private Color?[,] DrawData(bool[] dataStream, Color?[,] qr, out int usedMask)
        {
            usedMask = Mask;
            Color?[,] result = null;

            if (Mask < 0)
                result = DrawWithBestMask(dataStream, qr, out usedMask);
            else
            {
                result = DrawCorrectionLevelAndMaskCode(dataStream, qr, Mask);
                result = FillQR(dataStream, result, CodeInfo.Masks[Mask], true);
            }

            result[FreeSpacePoint, Size - FreeSpacePoint] = CodePalette.ModuleColor;
            return result;
        }

        private Color?[,] DrawWithBestMask(bool[] dataStream, Color?[,] qr, out int usedMask)
        {
            Logger.WriteLog("Search the best mask...");
            Color?[,] result = (Color?[,])qr.Clone();
            int minPenalty = int.MaxValue;
            usedMask = -1;

            for (int mi = 0; mi < CodeInfo.Masks.Length; mi++)
            {
                Logger.WriteLog("-------------------------------------------------------------------------");
                Logger.WriteLog($"Mask {mi}");
                Color?[,] filledQr = DrawCorrectionLevelAndMaskCode(dataStream, qr, mi);
                filledQr = FillQR(dataStream, filledQr, CodeInfo.Masks[mi]);
                int penalty = CalcPenaltyPoints(filledQr);

                if (penalty < minPenalty)
                {
                    usedMask = mi;
                    minPenalty = penalty;
                    result = filledQr;
                }
            }

            if (UpdateImageOnDraw)
            {
                Color?[,] filledQr = DrawCorrectionLevelAndMaskCode(dataStream, qr, usedMask);
                FillQR(dataStream, filledQr, CodeInfo.Masks[usedMask], true);
            }

            Logger.WriteLog("-------------------------------------------------------------------------");
            Logger.WriteLog($"The best mask {usedMask}");

            return result;
        }

        private Color?[,] DrawCorrectionLevelAndMaskCode(bool[] dataStream, Color?[,] qr, int mask)
        {
            int[] ClmCode = CodeInfo.CorrectionLevelAndMaskCode[(int)CorrectionLevel, mask];
            Color?[,] filled = (Color?[,])qr.Clone();

            for (int x = 0; x < SyncBandPosition; x++)
            {
                filled[x, FreeSpacePoint] = ClmCode[x] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;
                filled[x + Size - FreeSpacePoint, FreeSpacePoint] = ClmCode[FreeSpacePoint + x - 1] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;
            }

            filled[FreeSpacePoint - 1, FreeSpacePoint] = ClmCode[SyncBandPosition] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;
            filled[Size - 2, FreeSpacePoint] = ClmCode[13] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;
            filled[Size - 1, FreeSpacePoint] = ClmCode[14] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;

            for (int y = FreeSpacePoint; y > SyncBandPosition; y--)
                filled[FreeSpacePoint, y] = ClmCode[2 * FreeSpacePoint - y - 1] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;

            for (int y = SyncBandPosition - 1; y >= 0; y--)
                filled[FreeSpacePoint, y] = ClmCode[FreeSpacePoint + SyncBandPosition - y] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;

            for (int y = 0; y < FreeSpacePoint; y++)
                filled[FreeSpacePoint, Size - y - 1] = ClmCode[y] == 0 ? CodePalette.BackgroundColor : CodePalette.FormatColor;

            return filled;
        }

        private Color?[,] FillQR(bool[] dataStream, Color?[,] qr, Func<int, int, bool> mask, bool realtimeUpdate = false)
        {
            bool up = true;
            bool isRight = true;
            int posx = Size - 1;
            int posy = Size - 1;
            int i = 0;
            int dataLength = dataStream.Length;
            Color?[,] filled = (Color?[,])qr.Clone();

            while (true)
            {
                bool maskValue = mask(posx, posy);
                filled[posx, posy] = (i < dataLength && dataStream[i++]) ^ maskValue ? CodePalette.ModuleColor : CodePalette.BackgroundColor;

                while (filled[posx, posy].HasValue)
                {
                    if (isRight)
                    {
                        posx--;
                        isRight = false;
                        continue;
                    }

                    if (up)
                        posy--;
                    else
                        posy++;

                    posx++;
                    isRight = true;

                    if (posy < 0 || posy >= Size)
                    {
                        up = !up;

                        if (up)
                            posy--;
                        else
                            posy++;

                        posx -= 2;
                    }

                    if (posx == SyncBandPosition) // Skip vertical sync band
                        posx--;

                    if (posx < 0)
                    {
                        if (realtimeUpdate)
                            DoUpdateImageOnDraw(filled);

                        return filled;
                    }
                }

                if (realtimeUpdate)
                    DoUpdateImageOnDraw(filled);
            }
        }

        private BitmapSource Scaling(Color?[,] data)
        {
            int formatBytes = 4;
            int moduleSize = (int)Math.Truncate(CodeSize / (Size + FreeSpace * 2.0));
            Logger.WriteLog($"Scaling module size: {moduleSize}");

            if (moduleSize == 0)
                throw new Exception($"Image size cannot contain QR-code result!\r\nMinimum QR-code image size is {Size + FreeSpace * 2}px");

            int scaledSize = Size * moduleSize;
            Color[,] qrScaled = new Color[scaledSize, scaledSize];

            for (int y = 0; y < scaledSize; y++)
            {
                for (int x = 0; x < scaledSize; x++)
                {
                    if (data[x / moduleSize, y / moduleSize].HasValue)
                        qrScaled[x, y] = data[x / moduleSize, y / moduleSize].Value;
                    else
                        qrScaled[x, y] = CodePalette.MarkColor;
                }
            }

            int width = scaledSize + 2 * FreeSpace * moduleSize;
            int stride = width * formatBytes;
            WriteableBitmap bitmap = new WriteableBitmap(width, width, 96d, 96d, PixelFormats.Bgra32, null);
            Int32Rect rect = new Int32Rect(0, 0, width, width);
            int bytesCount = width * stride;
            byte[] bytes = new byte[bytesCount];

            for (int n = 0; n < bytesCount; n += 4)
            {
                Color c = CodePalette.BackgroundColor;
                bytes[n] = c.B;
                bytes[n + 1] = c.G;
                bytes[n + 2] = c.R;
                bytes[n + 3] = 255;
            }

            int i = stride * FreeSpace * moduleSize;

            for (int y = formatBytes * moduleSize; y < scaledSize + formatBytes * moduleSize; y++)
            {
                i += formatBytes * FreeSpace * moduleSize;

                for (int x = formatBytes * moduleSize; x < scaledSize + formatBytes * moduleSize; x++)
                {
                    Color c = qrScaled[x - formatBytes * moduleSize, y - formatBytes * moduleSize];
                    bytes[i++] = c.B;
                    bytes[i++] = c.G;
                    bytes[i++] = c.R;
                    bytes[i++] = 255;
                }

                i += formatBytes * FreeSpace * moduleSize;
            }

            bitmap.WritePixels(rect, bytes, stride, 0);
            bitmap.Freeze();

            return bitmap;
        }

        private void DoUpdateImageOnDraw(Color?[,] data)
        {
            if (!UpdateImageOnDraw)
                return;

            Thread.Sleep(Delay);
            Image = Scaling(data);
            OnImageUpdated?.Invoke();
        }

        private int CalcPenaltyPoints(Color?[,] qr)
        {
            Logger.WriteLog("CalcPenalty");

            int points = CalcPenaltyRule1(qr);
            points += CalcPenaltyRule2(qr);
            points += CalcPenaltyRule3(qr);
            points += CalcPenaltyRule4(qr);

            Logger.WriteLog($"CalcPenalty points: {points}");

            return points;
        }

        private int CalcPenaltyRule4(Color?[,] qr)
        {
            int filled = 0;
            int empty = 0;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (qr[x, y] == CodePalette.BackgroundColor)
                        empty++;
                    else
                        filled++;
                }
            }

            int points = (int)Math.Abs(filled * 100.0 / (filled + empty) - 50) * 2;
            Logger.WriteLog($"Rule 4 points: {points}");

            return points;
        }

        private int CalcPenaltyRule3(Color?[,] qr)
        {
            int points = 0;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size - 7; x++)
                {
                    if (qr[x, y] != CodePalette.BackgroundColor &&
                        qr[x + 1, y] == CodePalette.BackgroundColor &&
                        qr[x + 2, y] != CodePalette.BackgroundColor &&
                        qr[x + 3, y] != CodePalette.BackgroundColor &&
                        qr[x + 4, y] != CodePalette.BackgroundColor &&
                        qr[x + 5, y] == CodePalette.BackgroundColor &&
                        qr[x + 6, y] != CodePalette.BackgroundColor)
                    {
                        if ((x < Size - VersionYPosition &&
                            qr[x + 7, y] == CodePalette.BackgroundColor &&
                            qr[x + 8, y] == CodePalette.BackgroundColor &&
                            qr[x + 9, y] == CodePalette.BackgroundColor &&
                            qr[x + 10, y] == CodePalette.BackgroundColor
                            ) ||
                            (x > 3 &&
                            qr[x - 1, y] == CodePalette.BackgroundColor &&
                            qr[x - 2, y] == CodePalette.BackgroundColor &&
                            qr[x - 3, y] == CodePalette.BackgroundColor &&
                            qr[x - 4, y] == CodePalette.BackgroundColor
                            ))
                        {
                            points += 40;
                        }
                    }
                }
            }

            for (int x = 0; x < Size; x++)
            {
                for (int y = 0; y < Size - 7; y++)
                {
                    if (qr[x, y] != CodePalette.BackgroundColor &&
                        qr[x, y + 1] == CodePalette.BackgroundColor &&
                        qr[x, y + 2] != CodePalette.BackgroundColor &&
                        qr[x, y + 3] != CodePalette.BackgroundColor &&
                        qr[x, y + 4] != CodePalette.BackgroundColor &&
                        qr[x, y + 5] == CodePalette.BackgroundColor &&
                        qr[x, y + 6] != CodePalette.BackgroundColor)
                    {
                        if ((y < Size - VersionYPosition &&
                                qr[x, y + 7] == CodePalette.BackgroundColor &&
                                qr[x, y + 8] == CodePalette.BackgroundColor &&
                                qr[x, y + 9] == CodePalette.BackgroundColor &&
                                qr[x, y + 10] == CodePalette.BackgroundColor
                            ) ||
                            (y > 3 &&
                                qr[x, y - 1] == CodePalette.BackgroundColor &&
                                qr[x, y - 2] == CodePalette.BackgroundColor &&
                                qr[x, y - 3] == CodePalette.BackgroundColor &&
                                qr[x, y - 4] == CodePalette.BackgroundColor
                            ))
                        {
                            points += 40;
                        }
                    }
                }
            }

            Logger.WriteLog($"Rule 3 points: {points}");

            return points;
        }

        private int CalcPenaltyRule2(Color?[,] qr)
        {
            int points = 0;

            for (int y = 0; y < Size - 1; y++)
            {
                for (int x = 0; x < Size - 1; x++)
                {
                    if ((qr[x, y] != CodePalette.BackgroundColor && qr[x + 1, y] != CodePalette.BackgroundColor &&
                        qr[x, y + 1] != CodePalette.BackgroundColor && qr[x + 1, y + 1] != CodePalette.BackgroundColor) ||
                        (qr[x, y] == CodePalette.BackgroundColor && qr[x + 1, y] == CodePalette.BackgroundColor &&
                        qr[x, y + 1] == CodePalette.BackgroundColor && qr[x + 1, y + 1] == CodePalette.BackgroundColor))
                    {
                        points += 3;
                    }
                }
            }

            Logger.WriteLog($"Rule 2 points: {points}");

            return points;
        }

        private int CalcPenaltyRule1(Color?[,] qr)
        {
            int points = 0;
            int emptyLength;
            int filledLength;
            bool emptySequence;

            for (int y = 0; y < Size; y++)
            {
                emptyLength = 0;
                filledLength = 0;
                emptySequence = qr[0, y] == CodePalette.BackgroundColor;

                for (int x = 0; x < Size; x++)
                {
                    if (qr[x, y] != CodePalette.BackgroundColor)
                    {
                        if (!emptySequence)
                            filledLength++;
                        else
                        {
                            emptySequence = false;

                            if (emptyLength >= 5)
                                points += emptyLength - 2;

                            emptyLength = 0;
                        }
                    }
                    else
                    {
                        if (emptySequence)
                            emptyLength++;
                        else
                        {
                            emptySequence = true;

                            if (filledLength >= 5)
                                points += filledLength - 2;

                            filledLength = 0;
                        }
                    }
                }
            }

            for (int x = 0; x < Size; x++)
            {
                emptyLength = 0;
                filledLength = 0;
                emptySequence = qr[x, 0] == CodePalette.BackgroundColor;

                for (int y = 0; y < Size; y++)
                {
                    if (qr[x, y] != CodePalette.BackgroundColor)
                    {
                        if (!emptySequence)
                            filledLength++;
                        else
                        {
                            emptySequence = false;

                            if (emptyLength >= 5)
                                points += emptyLength - 2;

                            emptyLength = 0;
                        }
                    }
                    else
                    {
                        if (emptySequence)
                            emptyLength++;
                        else
                        {
                            emptySequence = true;

                            if (filledLength >= 5)
                                points += filledLength - 2;

                            filledLength = 0;
                        }
                    }
                }
            }

            Logger.WriteLog($"Rule 1 points: {points}");

            return points;
        }

        private void DrawVersion(Color?[,] qr)
        {
            if (!CodeInfo.VersionCode.ContainsKey(Version))
                return;

            int[] code = CodeInfo.VersionCode[Version];
            int pos = 0;

            for (int y = Size - VersionYPosition; y < Size - FreeSpacePoint; y++)
            {
                for (int x = 0; x < SyncBandPosition; x++)
                {
                    if (code[pos] == 1)
                    {
                        qr[x, y] = CodePalette.VersionColor;
                        qr[y, x] = CodePalette.VersionColor;
                    }
                    else
                    {
                        qr[x, y] = CodePalette.BackgroundColor;
                        qr[y, x] = CodePalette.BackgroundColor;
                    }

                    pos++;
                }
            }
        }

        private void DrawAlignmentPatterns(Color?[,] qr)
        {
            List<int> AlignPos = CodeInfo.AlignmentPatternsPositions[Version - 1];

            if (AlignPos == null)
                return;

            foreach (int centerY in AlignPos)
            {
                foreach (int centerX in AlignPos)
                {
                    if ((centerX >= Size - FreeSpacePoint && centerY <= FreeSpacePoint) ||
                        (centerY >= Size - FreeSpacePoint && centerX <= FreeSpacePoint) ||
                        (centerX == centerY && centerX <= FreeSpacePoint))
                    {
                        continue;
                    }

                    for (int colorIndex = 0; colorIndex < 2; colorIndex++)
                    {
                        for (int x = centerX - colorIndex - 1; x <= centerX + colorIndex + 1; x++)
                        {
                            qr[x, centerY - colorIndex - 1] = colorIndex == 0 ? CodePalette.BackgroundColor : CodePalette.AlignmentPatternColor;
                            qr[x, centerY + colorIndex + 1] = colorIndex == 0 ? CodePalette.BackgroundColor : CodePalette.AlignmentPatternColor;
                        }

                        for (int y = centerY - colorIndex - 1; y <= centerY + colorIndex + 1; y++)
                        {
                            qr[centerX - colorIndex - 1, y] = colorIndex == 0 ? CodePalette.BackgroundColor : CodePalette.AlignmentPatternColor;
                            qr[centerX + colorIndex + 1, y] = colorIndex == 0 ? CodePalette.BackgroundColor : CodePalette.AlignmentPatternColor;
                        }
                    }

                    qr[centerX, centerY] = CodePalette.AlignmentPatternColor;
                }
            }
        }

        private void DrawSearchPatterns(Color?[,] qr)
        {
            for (int x = 0; x < SearchPatternSize; x++)
            {
                qr[x, 0] = CodePalette.SearchPatternColor;
                qr[x, SyncBandPosition] = CodePalette.SearchPatternColor;
                qr[x, Size - 1] = CodePalette.SearchPatternColor;
                qr[x, Size - SearchPatternSize] = CodePalette.SearchPatternColor;
                qr[Size - x - 1, 0] = CodePalette.SearchPatternColor;
                qr[Size - x - 1, SyncBandPosition] = CodePalette.SearchPatternColor;

                if (x > 1 && x < 5)
                {
                    qr[x, 2] = CodePalette.SearchPatternColor;
                    qr[x, 3] = CodePalette.SearchPatternColor;
                    qr[x, 4] = CodePalette.SearchPatternColor;
                    qr[x, Size - 3] = CodePalette.SearchPatternColor;
                    qr[x, Size - 4] = CodePalette.SearchPatternColor;
                    qr[x, Size - 5] = CodePalette.SearchPatternColor;
                    qr[Size - x - 1, 2] = CodePalette.SearchPatternColor;
                    qr[Size - x - 1, 3] = CodePalette.SearchPatternColor;
                    qr[Size - x - 1, 4] = CodePalette.SearchPatternColor;
                }
            }

            for (int y = 0; y < SearchPatternSize; y++)
            {
                qr[0, y] = CodePalette.SearchPatternColor;
                qr[6, y] = CodePalette.SearchPatternColor;
                qr[Size - SearchPatternSize, y] = CodePalette.SearchPatternColor;
                qr[Size - 1, y] = CodePalette.SearchPatternColor;

                if (y > 1 && y < 5)
                {
                    qr[Size - 3, y] = CodePalette.SearchPatternColor;
                    qr[Size - 4, y] = CodePalette.SearchPatternColor;
                    qr[Size - 5, y] = CodePalette.SearchPatternColor;
                }
            }

            for (int y = Size - SearchPatternSize; y < Size; y++)
            {
                qr[0, y] = CodePalette.SearchPatternColor;
                qr[SyncBandPosition, y] = CodePalette.SearchPatternColor;
            }
        }

        private void DrawSyncBands(Color?[,] qr)
        {
            for (int x = FreeSpacePoint; x < Size - FreeSpacePoint; x++)
            {
                if (x % 2 == 0)
                {
                    qr[x, SyncBandPosition] = CodePalette.BackgroundColor;
                    qr[SyncBandPosition, x] = CodePalette.BackgroundColor;
                    continue;
                }

                qr[x, SyncBandPosition] = CodePalette.SyncBandColor;
                qr[SyncBandPosition, x] = CodePalette.SyncBandColor;
            }
        }
    }
}
