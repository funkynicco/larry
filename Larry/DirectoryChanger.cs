using System;

namespace Larry
{
    public class DirectoryChanger : IDisposable
    {
        private readonly string _oldDirectory;
        private bool _isChanged = false;

        public DirectoryChanger()
        {
            _oldDirectory = Environment.CurrentDirectory;
        }

        public DirectoryChanger(string directory) :
            this()
        {
            Change(directory);
        }

        public void Change(string directory)
        {
            Environment.CurrentDirectory = directory;
            _isChanged = true;
        }

        public void Reset()
        {
            if (_isChanged)
            {
                Environment.CurrentDirectory = _oldDirectory;
                _isChanged = false;
            }
        }

        public void Dispose()
        {
            Reset();
        }
    }
}
