using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System.Collections.Generic;
using System.Diagnostics;
using TorchSharp.Tensor;
using TorchSharp.NN;
using static TorchSharp.NN.Modules;
using static TorchSharp.NN.Functions;

namespace TorchSharp.Examples
{

    /// <summary>
    /// This example is based on the PyTorch tutorial at:
    /// 
    /// https://pytorch.org/tutorials/beginner/transformer_tutorial.html
    ///
    /// It relies on the WikiText2 dataset, which can be downloaded at:
    ///
    /// https://s3.amazonaws.com/research.metamind.io/wikitext/wikitext-2-v1.zip
    ///
    /// </summary>
    public class SequenceToSequence
    {
        // This path assumes that you're running this on Windows.
        private readonly static string _dataLocation = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "..", "Downloads", "wikitext-2-v1");

        private const long emsize = 200;
        private const long nhid = 200;
        private const long nlayers = 2;
        private const long nhead = 2;
        private const double dropout = 0.2;

        private const int batch_size = 64;
        private const int eval_batch_size = 32;

        private const int epochs = 50;

        static void Main(string[] args)

        {
            Torch.SetSeed(1);

            var cwd = Environment.CurrentDirectory;

            var device = Torch.IsCudaAvailable() ? Device.CUDA : Device.CPU;
            Console.WriteLine($"Running SequenceToSequence on {device.Type.ToString()}");

            var vocab_iter = TorchText.Datasets.WikiText2("train", _dataLocation);
            var tokenizer = TorchText.Data.Utils.get_tokenizer("basic_english");

            var counter = new TorchText.Vocab.Counter<string>();
            foreach (var item in vocab_iter) {
                counter.update(tokenizer(item));
            }

            var vocab = new TorchText.Vocab.Vocab(counter);

            var (train_iter, valid_iter, test_iter) = TorchText.Datasets.WikiText2(_dataLocation);

            var train_data = Batchify(ProcessInput(train_iter, tokenizer, vocab), batch_size).to(device);
            var valid_data = Batchify(ProcessInput(valid_iter, tokenizer, vocab), eval_batch_size).to(device);
            var test_data = Batchify(ProcessInput(test_iter, tokenizer, vocab), eval_batch_size).to(device);

            var bptt = 32;

            var (data, targets) = GetBatch(train_data, 0, bptt);

            var ntokens = vocab.Count;

            var model = new TransformerModel(ntokens, emsize, nhead, nhid, nlayers, dropout).to(device);
            var loss = cross_entropy_loss();
            var lr = 2.50;
            var optimizer = NN.Optimizer.SGD(model.parameters(), lr);
            var scheduler = NN.Optimizer.StepLR(optimizer, 1, 0.95, last_epoch: 15);

            var totalTime = new Stopwatch();
            totalTime.Start();

            foreach (var epoch in Enumerable.Range(1, epochs)) {

                var sw = new Stopwatch();
                sw.Start();

                train(epoch, train_data, model, loss, bptt, ntokens, optimizer);

                var val_loss = evaluate(valid_data, model, loss, lr, bptt, ntokens, optimizer);
                sw.Stop();

                Console.WriteLine($"\nEnd of epoch: {epoch} | lr: {scheduler.LearningRate:0.00} | time: {sw.Elapsed.TotalSeconds:0.0}s | loss: {val_loss:0.00}\n");
                scheduler.step();
            }

            var tst_loss = evaluate(test_data, model, loss, lr, bptt, ntokens, optimizer);
            totalTime.Stop();

            Console.WriteLine($"\nEnd of training | time: {totalTime.Elapsed.TotalSeconds:0.0}s | loss: {tst_loss:0.00}\n");
        }

        private static void train(int epoch, TorchTensor train_data, TransformerModel model, Loss criterion, int bptt, int ntokens, Optimizer optimizer)
        {
            model.Train();

            var total_loss = 0.0f;

            var src_mask = model.GenerateSquareSubsequentMask(bptt);

            var batch = 0;
            var log_interval = 200;

            var tdlen = train_data.shape[0];

            for (int i = 0; i < tdlen - 1; batch++, i += bptt) {

                var (data, targets) = GetBatch(train_data, i, bptt);
                optimizer.zero_grad();

                if (data.shape[0] != bptt) {
                    src_mask.Dispose();
                    src_mask = model.GenerateSquareSubsequentMask(data.shape[0]);
                }

                var output = model.forward(data, src_mask);
                var loss = criterion(output.view(-1, ntokens), targets);
                {
                    loss.backward();
                    model.parameters().clip_grad_norm(0.5);
                    optimizer.step();

                    total_loss += loss.to(Device.CPU).DataItem<float>();
                }

                GC.Collect();

                if (batch % log_interval == 0 && batch > 0) {
                    var cur_loss = total_loss / log_interval;
                    Console.WriteLine($"epoch: {epoch} | batch: {batch} / {tdlen/bptt} | loss: {cur_loss:0.00}");
                    total_loss = 0;
                }
            }
        }

        private static double evaluate(TorchTensor eval_data, TransformerModel model, Loss criterion, double lr, int bptt, int ntokens, Optimizer optimizer)
        {
            model.Eval();

            var total_loss = 0.0f;
            var src_mask = model.GenerateSquareSubsequentMask(bptt);
            var batch = 0;
            
            for (int i = 0; i < eval_data.shape[0] - 1; batch++, i += bptt) {

                var (data, targets) = GetBatch(eval_data, i, bptt);
                if (data.shape[0] != bptt) {
                    src_mask.Dispose();
                    src_mask = model.GenerateSquareSubsequentMask(data.shape[0]);
                }
                var output = model.forward(data, src_mask);
                var loss = criterion(output.view(-1, ntokens), targets); 
                total_loss += data.shape[0] * loss.to(Device.CPU).DataItem<float>();

                data.Dispose();
                targets.Dispose();

                GC.Collect();
            }

            return total_loss / eval_data.shape[0];
        }

        static TorchTensor ProcessInput(IEnumerable<string> iter, Func<string, IEnumerable<string>> tokenizer, TorchText.Vocab.Vocab vocab)
        {
            List<TorchTensor> data = new List<TorchTensor>();
            foreach (var item in iter) {
                List<long> itemData = new List<long>();
                foreach (var token in tokenizer(item)) {
                    itemData.Add(vocab[token]);
                }
                data.Add(Int64Tensor.from(itemData.ToArray()));
            }
            return data.Where(t => t.NumberOfElements > 0).ToArray().cat(0);
        }

        static TorchTensor Batchify(TorchTensor data, int batch_size)
        {
            var nbatch = data.shape[0] / batch_size;
            var d2 = data.narrow(0, 0, nbatch * batch_size).view(batch_size, -1).t();
            return d2.contiguous();
        }

        static (TorchTensor, TorchTensor) GetBatch(TorchTensor source, int index, int bptt)
        {
            var len = Math.Min(bptt, source.shape[0] - 1 - index);
            var data = source[TorchTensorIndex.Slice(index, index + len)];
            var target = source[TorchTensorIndex.Slice(index + 1, index + 1 + len)].reshape(-1);
                return (data, target);
        }

        class TransformerModel : CustomModule
        {
            private TransformerEncoder transformer_encoder;
            private PositionalEncoding pos_encoder;
            private Embedding encoder;
            private Linear decoder;

            private long ninputs;
            private Device device;

            public TransformerModel(long ntokens, long ninputs, long nheads, long nhidden, long nlayers, double dropout = 0.5) : base("Transformer")
            {
                this.ninputs = ninputs;

                pos_encoder = new PositionalEncoding(ninputs, dropout);
                var encoder_layers = TransformerEncoderLayer(ninputs, nheads, nhidden, dropout);
                transformer_encoder = TransformerEncoder(encoder_layers, nlayers);
                encoder = Embedding(ntokens, ninputs);
                decoder = Linear(ninputs, ntokens);
                InitWeights();

                RegisterComponents();
            }

            public TorchTensor GenerateSquareSubsequentMask(long size)
            {
                var mask = (Float32Tensor.ones(new long[] { size, size }) == 1).triu().transpose(0, 1);
                return mask.to_type(ScalarType.Float32).masked_fill(mask == 0, float.NegativeInfinity).masked_fill(mask == 1, 0.0f).to(device);
            }

            private void InitWeights()
            {
                var initrange = 0.1;

                Init.uniform(encoder.Weight, -initrange, initrange);
                Init.zeros(decoder.Bias);
                Init.uniform(decoder.Weight, -initrange, initrange);
            }

            public override TorchTensor forward(TorchTensor t)
            {
                throw new NotImplementedException("single-argument forward()");
            }

            public TorchTensor forward(TorchTensor t, TorchTensor mask)
            {
                var src = pos_encoder.forward(encoder.forward(t) * MathF.Sqrt(ninputs));
                var enc = transformer_encoder.forward(src, mask);
                return decoder.forward(enc);
            }

            public new TransformerModel to(Device device)
            {
                base.to(device);
                this.device = device;
                return this;
            }
        }

        class PositionalEncoding : CustomModule
        {
            private Dropout dropout;
            private TorchTensor pe;

            public PositionalEncoding(long dmodel, double dropout, int maxLen = 5000) : base("PositionalEncoding")
            {
                this.dropout = Dropout(dropout);
                var pe = Float32Tensor.zeros(new long[] { maxLen, dmodel });
                var position = Float32Tensor.arange(0, maxLen, 1).unsqueeze(1);
                var divTerm = (Float32Tensor.arange(0, dmodel, 2) * (-Math.Log(10000.0) / dmodel)).exp();
                pe[TorchTensorIndex.Ellipsis, TorchTensorIndex.Slice(0, null, 2)] = (position * divTerm).sin();
                pe[TorchTensorIndex.Ellipsis, TorchTensorIndex.Slice(1, null, 2)] = (position * divTerm).cos();
                this.pe = pe.unsqueeze(0).transpose(0, 1);

                RegisterComponents();
            }

            public override TorchTensor forward(TorchTensor t)
            {
                var x = t + pe[TorchTensorIndex.Slice(null, t.shape[0]), TorchTensorIndex.Slice()];
                return dropout.forward(x);
            }
        }
    }
}
