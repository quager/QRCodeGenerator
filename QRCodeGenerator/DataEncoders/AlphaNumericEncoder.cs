using System;

namespace QRCodeGenerator
{
    internal class AlphaNumericEncoder : DataEncoder
    {
        protected override string ModeIndicator => "0010";

        protected override int DataLength { get; set; }

        public override string EncodedData => _encodedData.ToString();

        public override EncodingMode EncMode => EncodingMode.AlphaNumeric;

        public override void Encode(string data)
        {
            _encodedData.Clear();
            DataLength = data.Length;

            for (int i = 0; i < DataLength; i += 2)
            {
                char c = data[i];
                int value = CodeInfo.AlphaNumericString.IndexOf(c);
                int length = 6;

                if (i + 1 < DataLength)
                {
                    value *= 45;
                    value += CodeInfo.AlphaNumericString.IndexOf(data[i + 1]);
                    length = 11;
                }

                string str = Convert.ToString(value, 2).PadLeft(length, '0');
                _encodedData.Append(str);
            }
        }
    }
}
