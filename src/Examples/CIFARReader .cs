using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TorchSharp.Tensor;

namespace TorchSharp.Examples
{
    /// <summary>
    /// Data reader utility for datasets that follow the MNIST data set's layout:
    ///
    /// A number of single-channel (grayscale) images are laid out in a flat file with four 32-bit integers at the head.
    /// The format is documented at the bottom of the page at: http://yann.lecun.com/exdb/mnist/
    /// </summary>
    class CIFARReader : IEnumerable<(TorchTensor, TorchTensor)>, IDisposable
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="path">Path to the folder containing the image files.</param>
        /// <param name="test">True if this is a test set, otherwise false.</param>
        /// <param name="batch_size">The batch size</param>
        /// <param name="shuffle">Randomly shuffle the images.</param>
        /// <param name="device">The device, i.e. CPU or GPU to place the output tensors on.</param>
        public CIFARReader(string path, bool test, int batch_size = 32, bool shuffle = false, Device device = null)
        {
            // The MNIST data set is small enough to fit in memory, so let's load it there.

            var dataPath = Path.Combine(path, "cifar-10-batches-bin");

            if (test) {
                Size = ReadSingleFile(Path.Combine(dataPath, "test_batch.bin"), batch_size, shuffle, device);
            } else {
                Size += ReadSingleFile(Path.Combine(dataPath, "data_batch_1.bin"), batch_size, shuffle, device);
                Size += ReadSingleFile(Path.Combine(dataPath, "data_batch_2.bin"), batch_size, shuffle, device);
                Size += ReadSingleFile(Path.Combine(dataPath, "data_batch_3.bin"), batch_size, shuffle, device);
                Size += ReadSingleFile(Path.Combine(dataPath, "data_batch_4.bin"), batch_size, shuffle, device);
                Size += ReadSingleFile(Path.Combine(dataPath, "data_batch_5.bin"), batch_size, shuffle, device);
            }
        }

        private int ReadSingleFile(string path, int batch_size, bool shuffle, Device device)
        {
            const int height = 32;
            const int width = 32;
            const int channels = 3;
            const int count = 10000;

            byte[] dataBytes = File.ReadAllBytes(path);

            if (dataBytes.Length != (1 + channels * height * width) * count)
                throw new InvalidDataException($"Not a proper CIFAR10 file: {path}");

            // Set up the indices array.
            Random rnd = new Random();
            var indices = !shuffle ?
                Enumerable.Range(0, count).ToArray() :
                Enumerable.Range(0, count).OrderBy(c => rnd.Next()).ToArray();

            var imgSize = channels * height * width;

            // Go through the data and create tensors
            for (var i = 0; i < count;) {

                var take = Math.Min(batch_size, Math.Max(0, count - i));

                if (take < 1) break;

                var dataTensor = Float32Tensor.zeros(new long[] { take, imgSize }, device);
                var lablTensor = Int64Tensor.zeros(new long[] { take }, device);

                // Take
                for (var j = 0; j < take; j++) {
                    var idx = indices[i++];
                    var lblStart = idx * (1 + imgSize);
                    var imgStart = lblStart + 1;

                    lablTensor[j] = Int64Tensor.from(dataBytes[lblStart]);

                    var floats = dataBytes[imgStart..(imgStart + imgSize)].Select(b => (float)b).ToArray();
                    using (var inputTensor = Float32Tensor.from(floats))
                        dataTensor.index_put_(new TorchTensorIndex[] { TorchTensorIndex.Single(j) }, inputTensor);
                }

                data.Add(dataTensor.reshape(take, channels, height, width));
                dataTensor.Dispose();
                labels.Add(lablTensor);
            }

            return count;
        }

        public int Size { get; set; }

        private List<TorchTensor> data = new List<TorchTensor>();
        private List<TorchTensor> labels = new List<TorchTensor>();

        public IEnumerator<(TorchTensor, TorchTensor)> GetEnumerator()
        {
            return new CIFAREnumerator(data, labels);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Dispose()
        {
            data.ForEach(d => d.Dispose());
            labels.ForEach(d => d.Dispose());
        }

        private class CIFAREnumerator : IEnumerator<(TorchTensor, TorchTensor)>
        {
            public CIFAREnumerator(List<TorchTensor> data, List<TorchTensor> labels)
            {
                this.data = data;
                this.labels = labels;
            }

            public (TorchTensor, TorchTensor) Current {
                get {
                    if (curIdx == -1) throw new InvalidOperationException("Calling 'Current' before 'MoveNext()'");
                    return (data[curIdx], labels[curIdx]);
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                curIdx += 1;
                return curIdx < data.Count;
            }

            public void Reset()
            {
                curIdx = -1;
            }

            private int curIdx = -1;
            private List<TorchTensor> data = null;
            private List<TorchTensor> labels = null;
        }
    }
}
