// Copyright (c) Microsoft Corporation and contributors.  All Rights Reserved.  See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TorchSharp
{
    using Debug = System.Diagnostics.Debug;

    public static class Torch
    {
        const string libtorchPackageVersion = "1.8.0.7";
        const string cudaVersion = "11.1";

        [DllImport("LibTorchSharp")]
        private static extern void THSTorch_manual_seed(long seed);

        [DllImport("LibTorchSharp")]
        private static extern IntPtr THSGenerator_manual_seed(long seed);

        public static void SetSeed(long seed)
        {
            TryInitializeDeviceType(DeviceType.CUDA);
            THSTorch_manual_seed(seed);
        }

        public static TorchGenerator ManualSeed(long seed)
        {
            TryInitializeDeviceType(DeviceType.CUDA);
            return new TorchGenerator(THSGenerator_manual_seed(seed));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetDllDirectory(string lpPathName);


        static string nativeRid =>
            (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? "win-x64" :
            (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) ? "linux-x64" :
            (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) ? "osx-x64" :
            "any";

        static string nativeGlob =>
            (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? @".*\.dll" :
            (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) ? @".*\.dynlib\.*" :
            // must match
            //   lib.so
            //   lib.so.1
            //   lib.so.11.0
            //   lib.so.11.1
            @".*\.so(\.\d*)*";
        static bool nativeBackendLoaded = false;
        static bool nativeBackendCudaLoaded = false;


        [System.Flags]
        public enum LoadLibraryFlags : uint
        {
            DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
            LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
            LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
            LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
            LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
            LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
            LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
            LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string dllToLoad, IntPtr hFile, LoadLibraryFlags flags);


        internal static bool TryLoadNativeLibraryFromFile(string path) {
            bool ok = false;
            try {
                //ok = NativeLibrary.TryLoad(path, out var res);
                ok = LoadLibraryEx(path, IntPtr.Zero, 0) != IntPtr.Zero;
            }
            catch {
                ok = false;
            }
            return ok;
        }


        internal static bool TryLoadNativeLibraryByName(string name, System.Reflection.Assembly assembly)
        {
            bool ok = false;
            try {
                //ok = NativeLibrary.TryLoad(name, assembly, null, out var res);
                ok = LoadLibraryEx(name, IntPtr.Zero, LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LoadLibraryFlags.LOAD_LIBRARY_SEARCH_SYSTEM32) != IntPtr.Zero;
            } catch {
                ok = false;
            }
            return ok;
        }

        public static void LoadNativeBackend(bool useCudaBackend)
        {
            bool ok = false;
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var target = isWindows ? "LibTorchSharp.dll" : "libLibTorchSharp.so";
            if (!(useCudaBackend ? nativeBackendCudaLoaded : nativeBackendLoaded)) {
                Trace.WriteLine($"TorchSharp: LoadNativeBackend: Initialising native backend");

                // Workarounds for weird LibTorch native stuff
                // See https://github.com/pytorch/pytorch/issues/33415
                if (useCudaBackend) {
                    if (isWindows) {
                        Trace.WriteLine($"Try loading Windows cuda native components");
                        // Preloading these DLLs on windows seems to iron out problems where one native DLL
                        // requests a load of another through dynamic linking techniques.
                        //
                        TryLoadNativeLibraryByName("cudnn_adv_infer64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("cudnn_adv_train64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("cudnn_cnn_infer64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("cudnn_cnn_train64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("cudnn_ops_infer64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("cudnn_ops_train64_8", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("nvrtc-builtins64_111", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("caffe2_nvrtc", typeof(Torch).Assembly);
                        TryLoadNativeLibraryByName("nvrtc64_111_0", typeof(Torch).Assembly);
                    }
                    Trace.WriteLine($"TorchSharp: LoadNativeBackend: Try loading torch_cuda native component");
                    TryLoadNativeLibraryByName("torch_cuda", typeof(Torch).Assembly);
                } else {
                    Trace.WriteLine($"TorchSharp: LoadNativeBackend: Loading torch_cpu");
                    TryLoadNativeLibraryByName("torch_cpu", typeof(Torch).Assembly);
                }
                Trace.WriteLine($"TorchSharp: LoadNativeBackend: Loading LibTorchSharp");
                ok = TryLoadNativeLibraryByName("LibTorchSharp", typeof(Torch).Assembly);

                Trace.WriteLine($"TorchSharp: LoadNativeBackend: Loaded LibTorchSharp, ok = {ok}");
                // Try dynamic load from package directories
                var cpuRootPackage = "libtorch-cpu";
                var cudaRootPackage = $"libtorch-cuda-{cudaVersion}-{nativeRid}";
                if (!ok) {

                    Console.WriteLine($"TorchSharp: LoadNativeBackend: Native backend not found in application loading TorchSharp directly from packages directory.");
                    // See https://github.com/xamarin/TorchSharp/issues/169
                    //
                    // If we are loading in .NET Interactive or F# Interactive, these are in packages in separate
                    // package directories. For managed DLLs this works OK, but native DLLs do not load transitive dependencies.
                    //
                    // So we shadow copy the DLLs into the TorchSharp package, make a copy of the native DLL and continue
                    // with the dynamic load
                    //
                    // Assumed to be in ...\packages\torchsharp\0.3.0-local-debug-20200918\lib\net5.0\TorchSharp.dll
                    //
                    // TODO: on linux make these copies link not shadow-copy
                    var torchsharpLoc = Path.GetDirectoryName(typeof(Torch).Assembly.Location);
                    var packagesDir = Path.GetFullPath(Path.Combine(torchsharpLoc, "..", "..", "..", ".."));
                    var torchsharpHome = Path.GetFullPath(Path.Combine(torchsharpLoc, "..", ".."));
                    if (torchsharpLoc.Contains("torchsharp") && torchsharpLoc.Contains("lib") && Directory.Exists(packagesDir) && Directory.Exists(torchsharpHome)) {

                        var torchSharpVersion = Path.GetFileName(torchsharpHome); // really GetDirectoryName

                        if (useCudaBackend) {
                            var consolidatedDir = Path.Combine(torchsharpLoc, $"cuda-{cudaVersion}");
                            Console.WriteLine($"TorchSharp: LoadNativeBackend: Trying dynamic load for .NET/F# Interactive by consolidating native {cudaRootPackage}-* binaries to {consolidatedDir}...");
                            var cudaOk = CopyNativeComponentsIntoSingleDirectory(packagesDir, $"{cudaRootPackage}-*", libtorchPackageVersion, consolidatedDir);
                            if (cudaOk) {
                                Trace.WriteLine($"TorchSharp: LoadNativeBackend: Consolidating native LibTorchSharp binaries to {consolidatedDir}...");
                                cudaOk = CopyNativeComponentsIntoSingleDirectory(packagesDir, "torchsharp", torchSharpVersion, consolidatedDir);
                                if (cudaOk) {
                                    var consolidated = Path.Combine(consolidatedDir, target);
                                    Trace.WriteLine($"TorchSharp: LoadNativeBackend: Trying to load {consolidated}...");
                                    ok = TryLoadNativeLibraryFromFile(consolidated);
                                }
                            }
                            if (!cudaOk)
                                throw new NotSupportedException($"The {cudaRootPackage} package version {libtorchPackageVersion} is not restored on this system. If using F# Interactive or .NET Interactive you may need to add a reference to this package, e.g. \n    #r \"nuget: {cudaRootPackage}, {libtorchPackageVersion}\"");
                        }
                        else {
                            var consolidatedDir = Path.Combine(torchsharpLoc, $"cpu");
                            Console.WriteLine($"TorchSharp: LoadNativeBackend: Trying dynamic load for .NET/F# Interactive by consolidating native {cpuRootPackage}-* binaries to {consolidatedDir}...");
                            var cpuOk = CopyNativeComponentsIntoSingleDirectory(packagesDir, cpuRootPackage, libtorchPackageVersion, consolidatedDir);
                            if (cpuOk) {
                                Trace.WriteLine($"TorchSharp: LoadNativeBackend: Consolidating native LibTorchSharp binaries to {consolidatedDir}...");
                                cpuOk = CopyNativeComponentsIntoSingleDirectory(packagesDir, "torchsharp", torchSharpVersion, consolidatedDir);
                                if (cpuOk) {
                                    var consolidated = Path.Combine(consolidatedDir, target);
                                    Trace.WriteLine($"TorchSharp: LoadNativeBackend: Trying to load {consolidated}...");
                                    ok = TryLoadNativeLibraryFromFile(consolidated);
                                    Trace.WriteLine($"TorchSharp: LoadNativeBackend: ok = {ok}...");
                                }
                            }
                            if (!cpuOk)
                                throw new NotSupportedException($"The {cpuRootPackage} package version {libtorchPackageVersion} is not restored on this system. If using F# Interactive or .NET Interactive you may need to add a reference to this package, e.g. \n    #r \"nuget: {cpuRootPackage}, {libtorchPackageVersion}\"");
                        }
                    }
                }
                if (!ok)
                    throw new NotSupportedException($"This application uses TorchSharp but doesn't contain reference to either {cudaRootPackage} or {cpuRootPackage}, {libtorchPackageVersion}. Consider either referncing one of these packages or call System.Runtime.InteropServices.NativeLibrary.Load explicitly for a Python install or a download of libtorch.so/torch.dll. See https://github.com/xamarin/TorchSharp/issues/169.\"");

                // Record the successful load
                if (useCudaBackend)
                    nativeBackendCudaLoaded = true;
                else
                    nativeBackendLoaded = true;
            }
        }

        public static bool TryInitializeDeviceType(DeviceType deviceType)
        {
            LoadNativeBackend(deviceType == DeviceType.CUDA);
            if (deviceType == DeviceType.CUDA) {
                return CallTorchCudaIsAvailable();
            } else {
                return true;
            }
        }

        /// Copy all native runtime DLLs into single directory if it hasn't been done already
        private static bool CopyNativeComponentsIntoSingleDirectory(string packagesDir, string packagePattern, string packageVersion, string target)
        {
            // Some loads will fail due to missing dependencies but then
            // these will be resolved in subsequent iterations.
            Trace.WriteLine($"CopyNativeComponentsIntoSingleDirectory: packagesDir = {packagesDir}");
            if (Directory.Exists(packagesDir)) {
                var packages =
                    Directory.GetDirectories(packagesDir, packagePattern)
                       .Where(d => Directory.Exists(Path.Combine(d, packageVersion)))
                       .ToArray();

                if (packages.Length > 0) {
                    if (!Directory.Exists(target))
                        Directory.CreateDirectory(target);
                    foreach (var package in packages) {
                        var natives = Path.Combine(package, packageVersion, "runtimes", nativeRid, "native");
                        Trace.WriteLine($"CopyNativeComponentsIntoSingleDirectory: package={package}, natives={natives}, target={target}");
                        if (Directory.Exists(natives)) {
                            var nativeRegExp = new Regex("^"+nativeGlob+"$");
                            foreach (var file in Directory.GetFiles(natives).Where(path => nativeRegExp.IsMatch(path))) {
                                var targetFile = Path.Combine(target, Path.GetFileName(file));
                                if (!File.Exists(targetFile)) {
                                    Trace.WriteLine($"Copy {file} --> {targetFile}");
                                    File.Copy(file, targetFile);
                                }
                            }
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        public static void InitializeDeviceType(DeviceType deviceType)
        {
            if (!TryInitializeDeviceType(deviceType)) {
                throw new InvalidOperationException($"Torch device type {deviceType} did not initialise on the current machine.");
            }
        }

        public static Device InitializeDevice(Device device)
        {
            if (device == null)
                device = TorchSharp.Device.CPU;
            InitializeDeviceType(device.Type);
            return device;
        }

        public static Device Device(string description)
        {
            return new Device(description);
        }

        public static Device Device(DeviceType type, int index = -1)
        {
            return new Device(type, index);
        }

        [DllImport("LibTorchSharp")]
        private static extern bool THSTorchCuda_is_available();

        /// This must be a separate method to the failure to bind DllImport THSTorchCuda_is_available
        /// is not raised as early as a DllImportException
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CallTorchCudaIsAvailable()
        {
            return THSTorchCuda_is_available();
        }

        public static bool IsCudaAvailable()
        {
            TryInitializeDeviceType(DeviceType.CUDA);
            return CallTorchCudaIsAvailable();
        }

        [DllImport("LibTorchSharp")]
        private static extern bool THSTorchCuda_cudnn_is_available();

        public static bool IsCudnnAvailable()
        {
            TryInitializeDeviceType(DeviceType.CUDA);
            return THSTorchCuda_cudnn_is_available();
        }

        [DllImport("LibTorchSharp")]
        private static extern int THSTorchCuda_device_count();

        public static int CudaDeviceCount()
        {
            TryInitializeDeviceType(DeviceType.CUDA);
            return THSTorchCuda_device_count();
        }

        [DllImport("LibTorchSharp")]
        private static extern IntPtr THSTorch_get_and_reset_last_err();

        //[Conditional("DEBUG")]
        internal static void CheckForErrors()
        {
            var error = THSTorch_get_and_reset_last_err();

            if (error != IntPtr.Zero)
            {
                throw new ExternalException(Marshal.PtrToStringAnsi(error));
            }
        }
    }

    public enum DeviceType
    {
        CPU = 0,
        CUDA = 1, // CUDA.
        MKLDNN = 2, // Reserved for explicit MKLDNN
        OPENGL = 3, // OpenGL
        OPENCL = 4, // OpenCL
        IDEEP = 5, // IDEEP.
        HIP = 6, // AMD HIP
        FPGA = 7, // FPGA
        MSNPU = 8, // MSNPU
        XLA = 9 // XLA / TPU
    }
}
