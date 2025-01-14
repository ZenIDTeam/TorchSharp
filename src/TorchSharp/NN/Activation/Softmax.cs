// Copyright (c) Microsoft Corporation and contributors.  All Rights Reserved.  See License.txt in the project root for license information.
using System;
using System.Runtime.InteropServices;
using TorchSharp.Tensor;

namespace TorchSharp.NN
{
    /// <summary>
    /// This class is used to represent a Softmax module.
    /// </summary>
    public class Softmax : Module
    {
        internal Softmax (IntPtr handle, IntPtr boxedHandle) : base (handle, boxedHandle) { }

        [DllImport ("LibTorchSharp")]
        private static extern IntPtr THSNN_Softmax_forward (Module.HType module, IntPtr tensor);

        public TorchTensor forward (TorchTensor tensor)
        {
            var res = THSNN_Softmax_forward (handle, tensor.Handle);
            if (res == IntPtr.Zero) { Torch.CheckForErrors(); }
            return new TorchTensor (res);
        }

        public override string GetName ()
        {
            return typeof (Softmax).Name;
        }
    }

    public static partial class Modules
    {
        [DllImport ("LibTorchSharp")]
        extern static IntPtr THSNN_Softmax_ctor (long dim, out IntPtr pBoxedModule);

        /// <summary>
        /// Softmax
        /// </summary>
        /// <param name="dim">A dimension along which Softmax will be computed (so every slice along dim will sum to 1)</param>
        /// <returns></returns>
        static public Softmax Softmax(long dim)
        {
            var handle = THSNN_Softmax_ctor(dim, out var boxedHandle);
            if (handle == IntPtr.Zero) { Torch.CheckForErrors(); }
            return new Softmax (handle, boxedHandle);
        }
    }
    public static partial class Functions
    {
        /// <summary>
        /// Softmax
        /// </summary>
        /// <param name="x">The input tensor</param>
        /// <param name="dim">A dimension along which Softmax will be computed (so every slice along dim will sum to 1)</param>
        /// <returns></returns>
        static public TorchTensor Softmax (TorchTensor x, long dim)
        {
            using (var m = Modules.Softmax(dim)) {
                return m.forward (x);
            }
        }
    }

}
