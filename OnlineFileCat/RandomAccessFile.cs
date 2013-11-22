using System.IO;

namespace OnlineFileCat
{
    public class RandomAccessFile
    {
        private readonly FileStream _fileStream;

        public RandomAccessFile(FileStream stream)
        {
            _fileStream = stream;
        }

        //~RandomAccessFile()
        //{
        //    Close();
        //}

        public void Close()
        {
            if(_fileStream.CanWrite)
                _fileStream.Flush();
            _fileStream.Close();
        }

        public void WriteByte(byte b)
        {
            _fileStream.WriteByte(b);
            _fileStream.Flush();
        }

        public void WriteByteArray(byte[] buffer)
        {
            _fileStream.Write(buffer, 0, buffer.Length);
            _fileStream.Flush();
        }

        public byte ReadByte()
        {
            return (byte) _fileStream.ReadByte();
        }

        public void ReadByteArray(byte[] buffer)
        {
            _fileStream.Read(buffer, 0, buffer.Length);
        }

        public void Seek(int position)
        {
            _fileStream.Seek(position, SeekOrigin.Begin);
        }

        public string GetFileName()
        {
            return _fileStream.Name;
        }

        public void Flush()
        {
            _fileStream.Flush();
        }
    }
}
