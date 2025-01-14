// Copyright (c) Microsoft Corporation and contributors.  All Rights Reserved.  See License.txt in the project root for license information.
using System;
using System.Runtime.InteropServices;
using TorchSharp.Tensor;

namespace TorchSharp.NN
{
    /// <summary>
    /// This class is used to represent a AdaptiveMaxPool3D module.
    /// </summary>
    public class AdaptiveMaxPool3d : Module
    {
        internal AdaptiveMaxPool3d (IntPtr handle, IntPtr boxedHandle) : base (handle, boxedHandle)
        {
        }

        [DllImport ("LibTorchSharp")]
        private static extern IntPtr THSNN_AdaptiveMaxPool3d_forward (IntPtr module, IntPtr tensor);

        public TorchTensor forward (TorchTensor tensor)
        {
            var res = THSNN_AdaptiveMaxPool3d_forward (handle.DangerousGetHandle (), tensor.Handle);
            if (res == IntPtr.Zero) { Torch.CheckForErrors(); }
            return new TorchTensor (res);
        }
    }
    public static partial class Modules
    {
        [DllImport ("LibTorchSharp")]
        extern static IntPtr THSNN_AdaptiveMaxPool3d_ctor (IntPtr psizes, int length, out IntPtr pBoxedModule);

        /// <summary>
        /// Applies a 3D adaptive max pooling over an input signal composed of several input planes.
        /// The output is of size D x H x W, for any input size.The number of output features is equal to the number of input planes.
        /// </summary>
        /// <param name="outputSize">The target output size of the image of the form D x H x W.
        /// Can be a tuple (D, H, W) or a single D for a cube D x D x D. D, H and W can be either a int, or null which means the size will be the same as that of the input.</param>
        /// <returns></returns>
        static public AdaptiveMaxPool3d AdaptiveMaxPool3d (long[] outputSize)
        {
            unsafe {
                fixed (long* pkernelSize = outputSize) {
                    var handle = THSNN_AdaptiveMaxPool3d_ctor ((IntPtr)pkernelSize, outputSize.Length, out var boxedHandle);
                    if (handle == IntPtr.Zero) { Torch.CheckForErrors(); }
                    return new AdaptiveMaxPool3d (handle, boxedHandle);
                }
            }
        }
    }

    public static partial class Functions
    {
        /// <summary>
        /// Applies a 3D adaptive max pooling over an input signal composed of several input planes.
        /// The output is of size D x H x W, for any input size.The number of output features is equal to the number of input planes.
        /// </summary>
        /// <param name="x">The input tensor</param>
        /// <param name="outputSize">The target output size of the image of the form D x H x W.
        /// Can be a tuple (D, H, W) or a single D for a cube D x D x D. D, H and W can be either a int, or null which means the size will be the same as that of the input.</param>
        /// <returns></returns>
        static public TorchTensor AdaptiveMaxPool3d (TorchTensor x, long[] outputSize)
        {
            using (var d = Modules.AdaptiveMaxPool3d (outputSize)) {
                return d.forward (x);
            }
        }
    }
}
