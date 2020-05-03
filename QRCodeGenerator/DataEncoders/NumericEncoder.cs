using System;
using System.Text;

namespace QRCodeGenerator
{
    internal class NumericEncoder : DataEncoder
    {
        protected override string ModeIndicator => "0001";

        protected override int DataLength { get; set; }

        public override string EncodedData => _encodedData.ToString();

        public override EncodingMode EncMode => EncodingMode.Numeric;

        public override void Encode(string data)
        {
            _encodedData.Clear();
            StringBuilder triplet = new StringBuilder();

            string str;
            DataLength = data.Length;

            foreach (char c in data)
            {
                if (triplet.Length == 3)
                {
                    str = FormatData(triplet.ToString());
                    _encodedData.Append(str);
                    triplet.Clear();
                }

                triplet.Append(c);
            }

            if (triplet.Length > 0)
            {
                str = FormatData(triplet.ToString());
                _encodedData.Append(str);
            }
        }

        private static string FormatData(string data)
        {
            int length;
            switch (data.Length)
            {
                case 1:
                    length = 4;
                    break;
                case 2:
                    length = 7;
                    break;
                default:
                    length = 10;
                    break;
            }

            int value = int.Parse(data);
            string str = Convert.ToString(value, 2).PadLeft(length, '0');

            return str;
        }
    }
}
