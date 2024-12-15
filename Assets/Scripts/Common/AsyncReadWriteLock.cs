using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Common {
    public class AsyncReadWriteLock {
        private readonly Queue<UniTaskCompletionSource<bool>> _readWaiters = new();

        private readonly Queue<UniTaskCompletionSource<bool>> _writeWaiters = new();
        private bool _isWriterActive;
        private int _readerCount;

        public UniTask EnterReadLockAsync() {
            lock (this) {
                if (!_isWriterActive && _writeWaiters.Count == 0) {
                    _readerCount++;
                    return UniTask.CompletedTask;
                }

                var tcs = new UniTaskCompletionSource<bool>();
                _readWaiters.Enqueue(tcs);
                return tcs.Task;
            }
        }

        public void ExitReadLock() {
            UniTaskCompletionSource<bool> nextWriter = null;
            lock (this) {
                _readerCount--;
                if (_readerCount == 0 && _writeWaiters.Count > 0) {
                    nextWriter = _writeWaiters.Dequeue();
                    _isWriterActive = true;
                }
            }

            nextWriter?.TrySetResult(true);
        }

        public UniTask EnterWriteLockAsync() {
            lock (this) {
                if (!_isWriterActive && _readerCount == 0) {
                    _isWriterActive = true;
                    return UniTask.CompletedTask;
                }

                var tcs = new UniTaskCompletionSource<bool>();
                _writeWaiters.Enqueue(tcs);
                return tcs.Task;
            }
        }

        public void ExitWriteLock() {
            List<UniTaskCompletionSource<bool>> readyReaders = null;
            UniTaskCompletionSource<bool> nextWriter = null;

            lock (this) {
                _isWriterActive = false;

                if (_writeWaiters.Count > 0) {
                    nextWriter = _writeWaiters.Dequeue();
                    _isWriterActive = true;
                } else if (_readWaiters.Count > 0) {
                    readyReaders = new List<UniTaskCompletionSource<bool>>(_readWaiters.Count);
                    while (_readWaiters.Count > 0) readyReaders.Add(_readWaiters.Dequeue());

                    _readerCount = readyReaders.Count;
                }
            }

            if (nextWriter != null)
                nextWriter.TrySetResult(true);
            else if (readyReaders != null)
                foreach (var reader in readyReaders)
                    reader.TrySetResult(true);
        }
    }
}