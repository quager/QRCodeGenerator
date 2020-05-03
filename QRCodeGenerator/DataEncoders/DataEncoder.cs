using System;
using System.Text;

namespace QRCodeGenerator
{
    internal abstract class DataEncoder
    {
        protected StringBuilder _encodedData = new StringBuilder();

        protected abstract string ModeIndicator { get; }

        protected abstract int DataLength { get; set; }

        public abstract string EncodedData { get; }

        public abstract EncodingMode EncMode { get; }

        public abstract void Encode(string data);

        public virtual void AddHeader(int lengthIndicatorLength)
        {
            string lengthIndicator = Convert.ToString(DataLength, 2).PadLeft(lengthIndicatorLength, '0');

            _encodedData.Insert(0, lengthIndicator);
            _encodedData.Insert(0, ModeIndicator);
        }
    }
}