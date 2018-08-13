using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Larry.Network
{
    public interface IRequestDataHost
    {
        void RemoveRequest(RequestData request);
    }

    public class RequestData : IDisposable
    {
        private readonly IRequestDataHost _owner;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public int Id { get; private set; }

        public bool IsFinished => _cancellationTokenSource.IsCancellationRequested;

        public object Data { get; private set; }

        public CancellationToken Token => _cancellationTokenSource.Token;

        public RequestData(IRequestDataHost owner, int id)
        {
            _owner = owner;
            Id = id;

            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _owner.RemoveRequest(this);
            _cancellationTokenSource.Dispose();
        }

        public void Complete<T>(T data)
        {
            Data = data;
            _cancellationTokenSource.Cancel();
        }

        public void Complete()
            => Complete<object>(null);
    }
}
