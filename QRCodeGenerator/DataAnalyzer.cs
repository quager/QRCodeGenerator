using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QRCodeGenerator
{
    internal static class DataAnalyzer
    {
        private class EncodingSequence
        {
            private StringBuilder _sb = new StringBuilder();
            private Level _correctionLevel;
            private int _version;

            public int Start { get; set; }

            public int Length => _sb.Length;

            public EncodingMode Enc { get; set; }

            public string EncodedData { get; private set; }

            public string Data => _sb.ToString();

            public EncodingSequence(Level correctionLevel, int version)
            {
                _correctionLevel = correctionLevel;
                _version = version;
            }

            public void AppendData(char c) => _sb.Append(c);

            public void Encode(int codeVersion)
            {
                DataEncoder encoder;

                switch (Enc)
                {
                    case EncodingMode.Numeric:
                        encoder = new NumericEncoder();
                        break;
                    case EncodingMode.AlphaNumeric:
                        encoder = new AlphaNumericEncoder();
                        break;
                    case EncodingMode.Bytes:
                        encoder = new BytesEncoder();
                        break;
                    default:
                        throw new Exception("Unsupported Encoding Mode!");
                }

                encoder.Encode(Data);
                int lengthIndicatorLength = CodeInfo.GetLengthIndicatorLength(codeVersion, Enc);
                encoder.AddHeader(lengthIndicatorLength);
                EncodedData = encoder.EncodedData;
            }

            public List<List<byte>> GetBlocks() => GetBlocks(EncodedData, _correctionLevel, _version);

            public static List<List<byte>> GetBlocks(string sequence, Level correctionLevel, int codeVersion)
            {
                List<byte> dataBlocks = CreateBlocks(sequence, correctionLevel, codeVersion);

                int add = 0;
                int blockSize = dataBlocks.Count;
                int nblocks = CodeInfo.NumberOfCorrectionBlocks[(int)correctionLevel, codeVersion - 1];
                List<List<byte>> blocks = new List<List<byte>>();

                if (nblocks > 1)
                {
                    add = dataBlocks.Count % nblocks;
                    blockSize = dataBlocks.Count / nblocks;
                }

                int pos = 0;

                for (int i = 0; i < nblocks; i++)
                {
                    int size = blockSize;

                    if (i >= nblocks - add)
                        size++;

                    List<byte> block = new List<byte>();

                    for (int n = 0; n < size; n++)
                        block.Add(dataBlocks[pos + n]);

                    pos += size;
                    blocks.Add(block);
                }

                return blocks;
            }

            private static List<byte> CreateBlocks(string sequence, Level correctionLevel, int codeVersion)
            {
                int i = 0;
                StringBuilder sb = new StringBuilder();
                List<byte> dataBlocks = new List<byte>();

                foreach (char c in sequence)
                {
                    if (i == 8)
                    {
                        i = 0;
                        byte value = BinaryToByte(sb.ToString());
                        dataBlocks.Add(value);
                        sb.Clear();
                    }

                    sb.Append(c);
                    i++;
                }

                int nblocks = CodeInfo.NumberOfDataWords[(int)correctionLevel, codeVersion - 1];
                if (dataBlocks.Count < nblocks)
                {
                    i += CodeInfo.Limiter.Length;
                    sb.Append(CodeInfo.Limiter);

                    if (i > 8)
                    {
                        i -= 8;
                        byte value = BinaryToByte(sb.ToString().Substring(0, 8));
                        dataBlocks.Add(value);
                        sb.Remove(0, 8);
                    }
                }

                if (i > 0)
                {
                    while (i < 8)
                    {
                        sb.Append('0');
                        i++;
                    }

                    byte value = BinaryToByte(sb.ToString());
                    dataBlocks.Add(value);
                }

                i = 0;

                while (dataBlocks.Count < nblocks)
                    dataBlocks.Add(CodeInfo.Extenders[i++ % 2]);

                if (dataBlocks.Count > nblocks)
                    throw new Exception("Too much data!");

                return dataBlocks;
            }

            private static byte BinaryToByte(string value)
            {
                int[] pow2 = { 1, 2, 4, 8, 16, 32, 64, 128 };
                byte result = 0;

                for (int i = 0; i < 8; i++)
                    result += (byte)(int.Parse(value[i].ToString()) * pow2[7 - i]);

                return result;
            }
        }

        public static List<List<byte>> GetEncodedData(string data, Level correctionLevel, out int codeVersion) =>
            GetEncodedData(data, correctionLevel, CodeInfo.MaxVersion, out codeVersion, true);

        public static List<List<byte>> GetEncodedData(string data, Level correctionLevel, int codeVersion, out int usedVersion,
            bool useMinVersion = false)
        {
            Logger.WriteLog("GetEncodedData");

            IEnumerable<EncodingSequence> dataSequences = SplitByDataType(data, correctionLevel, codeVersion);
            int resultLength = EncodeSequences(correctionLevel, codeVersion, useMinVersion, dataSequences, out List<List<byte>> encodedData, out int minVersion);

            List<List<byte>> bestResult = encodedData;
            int bestVersion = minVersion;
            int minLength = resultLength;
            string usedEncMode = "Combined";

            dataSequences = BytesOnly(data, correctionLevel, codeVersion);
            resultLength = EncodeSequences(correctionLevel, codeVersion, useMinVersion, dataSequences, out encodedData, out minVersion);

            if (resultLength < minLength)
            {
                bestResult = encodedData;
                bestVersion = minVersion;
                minLength = resultLength;
                usedEncMode = "Bytes";
            }

            if (TryNumericOnly(data, correctionLevel, codeVersion, out dataSequences))
            {
                resultLength = EncodeSequences(correctionLevel, codeVersion, useMinVersion, dataSequences, out encodedData, out minVersion);

                if (resultLength < minLength)
                {
                    bestResult = encodedData;
                    bestVersion = minVersion;
                    minLength = resultLength;
                    usedEncMode = "Numeric";
                }
            }

            if (TryAlphaNumericOnly(data, correctionLevel, codeVersion, out dataSequences))
            {
                resultLength = EncodeSequences(correctionLevel, codeVersion, useMinVersion, dataSequences, out encodedData, out minVersion);

                if (resultLength < minLength)
                {
                    bestResult = encodedData;
                    bestVersion = minVersion;
                    minLength = resultLength;
                    usedEncMode = "AlphaNumeric";
                }
            }

            if (bestVersion > codeVersion)
                throw new Exception("QR-Code version data capacity not enough!");

            usedVersion = useMinVersion ? bestVersion : codeVersion;
            Logger.WriteLog($"GetEncodedData used version = {usedVersion}, mode = {usedEncMode}");
            Logger.WriteLog("GetEncodedData encoded blocks:\r\n    " +
                string.Join("\r\n    ", bestResult.Select((x, i) => $"Block {i}. [{string.Join(" ", x.Select(d => Logger.ToHexString(d)))}]")));

            return bestResult;
        }

        private static int EncodeSequences(Level correctionLevel, int codeVersion, bool useMinVersion, IEnumerable<EncodingSequence> dataSequences,
            out List<List<byte>> encodedSequences, out int minVersion)
        {
            Logger.WriteLog("EncodeSequences input:\r\n    " + string.Join("\r\n    ", dataSequences.Select((x, i) => $"{i}. \"{x.Data}\" [{x.Enc}]")));

            int blocksCount = 0;
            StringBuilder sb = new StringBuilder();

            foreach (EncodingSequence sequence in dataSequences)
            {
                sequence.Encode(codeVersion);
                if (!useMinVersion)
                    sb.Append(sequence.EncodedData);

                blocksCount += (int)Math.Ceiling(sequence.EncodedData.Length / 8.0);
            }

            minVersion = GetCodeVersionForBlocksCount(blocksCount, correctionLevel);

            if (useMinVersion)
            {
                foreach (EncodingSequence sequence in dataSequences)
                {
                    sequence.Encode(minVersion);
                    sb.Append(sequence.EncodedData);
                }
            }

            int usedVersion = useMinVersion ? minVersion : codeVersion;
            encodedSequences = EncodingSequence.GetBlocks(sb.ToString(), correctionLevel, usedVersion);
            Logger.WriteLog("EncodeSequences result blocks:\r\n    " +
                string.Join("\r\n    ", encodedSequences.Select((x, i) => $"Block {i}. [{string.Join(" ", x.Select(d => Logger.ToHexString(d)))}]")));

            return sb.Length;
        }

        private static int GetCodeVersionForBlocksCount(int blocksCount, Level correctionLevel)
        {
            for (int version = 0; version < CodeInfo.MaxVersion; version++)
            {
                int count = CodeInfo.NumberOfDataWords[(int)correctionLevel, version];

                if (blocksCount <= count)
                    return version + 1;
            }

            throw new Exception("Too much data!");
        }

        private static IEnumerable<EncodingSequence> SplitByDataType(string data, Level correctionLevel, int version)
        {
            Logger.WriteLog("SplitByDataType");

            var result = new List<EncodingSequence>();
            EncodingSequence last = null;

            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];

                if (CheckNumeric(c))
                {
                    last = AddToSequences(result, c, i, EncodingMode.Numeric, last, correctionLevel, version);
                    continue;
                }

                if (CheckAlphaNumeric(c))
                //    && (last.Enc == EncodingMode.AlphaNumeric || (i < data.Length - 1 && CheckAlphaNumeric(data[i + 1]))))
                {
                    last = AddToSequences(result, c, i, EncodingMode.AlphaNumeric, last, correctionLevel, version);
                    continue;
                }

                last = AddToSequences(result, c, i, EncodingMode.Bytes, last, correctionLevel, version);
            }

            Logger.WriteLog("SplitByDataType sequences:\r\n    " + string.Join("\r\n    ", result.Select((x, i) => $"{i}. \"{x.Data}\" [{x.Enc}]")));

            return result;
        }

        private static IEnumerable<EncodingSequence> BytesOnly(string data, Level correctionLevel, int version)
        {
            if (data.Length == 0)
                return Enumerable.Empty<EncodingSequence>();

            Logger.WriteLog($"BytesOnly");

            EncodingSequence seq = new EncodingSequence(correctionLevel, version)
            {
                Start = 0,
                Enc = EncodingMode.Bytes
            };

            var result = new List<EncodingSequence> { seq };

            for (int i = 0; i < data.Length; i++)
                seq.AppendData(data[i]);

            return result;
        }

        private static bool TryNumericOnly(string data, Level correctionLevel, int version, out IEnumerable<EncodingSequence> sequences)
        {
            sequences = new List<EncodingSequence>();

            if (data.Length == 0)
                return false;

            Logger.WriteLog($"TryNumericOnly");

            EncodingSequence seq = new EncodingSequence(correctionLevel, version)
            {
                Start = 0,
                Enc = EncodingMode.Numeric
            };

            var result = new List<EncodingSequence> { seq };

            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (!CheckNumeric(c))
                    return false;

                seq.AppendData(c);
            }

            sequences = result;
            return true;
        }

        private static bool TryAlphaNumericOnly(string data, Level correctionLevel, int version, out IEnumerable<EncodingSequence> sequences)
        {
            sequences = new List<EncodingSequence>();

            if (data.Length == 0)
                return false;

            Logger.WriteLog($"TryAlphaNumericOnly");

            EncodingSequence seq = new EncodingSequence(correctionLevel, version)
            {
                Start = 0,
                Enc = EncodingMode.AlphaNumeric
            };

            var result = new List<EncodingSequence> { seq };

            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (!CheckAlphaNumeric(c))
                    return false;

                seq.AppendData(c);
            }

            sequences = result;
            return true;
        }

        private static EncodingSequence AddToSequences(List<EncodingSequence> result, char c, int index, EncodingMode enc, EncodingSequence last,
            Level correctionLevel, int version)
        {
            if (last == null || last.Enc != enc)
            {
                last = new EncodingSequence(correctionLevel, version)
                {
                    Start = index,
                    Enc = enc
                };

                last.AppendData(c);
                result.Add(last);
            }
            else
            if (last.Enc == enc)
                last.AppendData(c);

            return last;
        }

        private static bool CheckNumeric(char c) => char.IsDigit(c);

        private static bool CheckAlphaNumeric(char c) => CodeInfo.AlphaNumericString.Contains(c);
    }
}
