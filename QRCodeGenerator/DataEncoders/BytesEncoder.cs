using System;
using System.Text;

namespace QRCodeGenerator
{
    internal class BytesEncoder : DataEncoder
    {
        protected override string ModeIndicator => "0100";

        protected override int DataLength { get; set; }

        public override string EncodedData => _encodedData.ToString();

        public override EncodingMode EncMode => EncodingMode.Bytes;

        public override void Encode(string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            Encode(bytes);
        }

        public void Encode(byte[] data)
        {
            _encodedData.Clear();
            DataLength = data.Length;

            foreach (byte b in data)
            {
                string str = Convert.ToString(b, 2).PadLeft(8, '0');
                _encodedData.Append(str);
            }
        }
    }
}
