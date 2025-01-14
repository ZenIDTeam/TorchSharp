// Copyright (c) Microsoft Corporation and contributors.  All Rights Reserved.  See License.txt in the project root for license information.
using System;
using System.Runtime.InteropServices;
using TorchSharp.Tensor;

namespace TorchSharp.NN
{
    /// <summary>
    /// This class is used to represent a SELU module.
    /// </summary>
    public class SELU : Module
    {
        internal SELU (IntPtr handle, IntPtr boxedHandle) : base (handle, boxedHandle) { }

        [DllImport ("LibTorchSharp")]
        private static extern IntPtr THSNN_SELU_forward (Module.HType module, IntPtr tensor);

        public TorchTensor forward (TorchTensor tensor)
        {
            var res = THSNN_SELU_forward (handle, tensor.Handle);
            if (res == IntPtr.Zero) { Torch.CheckForErrors(); }
            return new TorchTensor (res);
        }

        public override string GetName ()
        {
            return typeof (SELU).Name;
        }
    }

    public static partial class Modules
    {
        [DllImport ("LibTorchSharp")]
        extern static IntPtr THSNN_SELU_ctor (bool inplace, out IntPtr pBoxedModule);

        /// <summary>
        /// Scaled Exponential Linear Unit
        /// </summary>
        /// <param name="inPlace">Do the operation in-place. Default: False</param>
        /// <returns></returns>
        static public SELU SELU(bool inPlace = false)
        {
            var handle = THSNN_SELU_ctor (inPlace, out var boxedHandle);
            if (handle == IntPtr.Zero) { Torch.CheckForErrors(); }
            return new SELU (handle, boxedHandle);
        }
    }
    public static partial class Functions
    {
        /// <summary>
        /// Scaled Exponential Linear Unit
        /// </summary>
        /// <param name="x">The input tensor</param>
        /// <param name="inPlace">Do the operation in-place. Default: False</param>
        /// <returns></returns>
        static public TorchTensor SELU(TorchTensor x, bool inPlace = false)
        {
            using (var m = Modules.SELU(inPlace)) {
                return m.forward (x);
            }
        }
    }

}
