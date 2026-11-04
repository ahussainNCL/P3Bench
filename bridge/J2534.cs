using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace P3Orbit
{
    internal sealed class Adapter
    {
        public string Name;
        public string Vendor;
        public string Library;
    }

    // 32-bit J2534 v04.04 registration view. Matching P3ToolClone, this only
    // discovers drivers; nothing is loaded until Open().
    internal static class AdapterDiscovery
    {
        public static List<Adapter> Find()
        {
            var result = new List<Adapter>();
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var root = machine.OpenSubKey(@"SOFTWARE\PassThruSupport.04.04"))
            {
                if (root == null) return result;
                foreach (string keyName in root.GetSubKeyNames())
                using (var key = root.OpenSubKey(keyName))
                {
                    if (key == null) continue;
                    var library = key.GetValue("FunctionLibrary") as string;
                    if (String.IsNullOrWhiteSpace(library)) continue;
                    result.Add(new Adapter
                    {
                        Name = key.GetValue("Name") as string ?? keyName,
                        Vendor = key.GetValue("Vendor") as string ?? "Unknown",
                        Library = Environment.ExpandEnvironmentVariables(library)
                    });
                }
            }
            return result;
        }
    }

    internal sealed class J2534Exception : Exception
    {
        internal readonly int Status;
        internal J2534Exception(string operation, int status)
            : base(operation + " failed: J2534 status " + status + ".")
        {
            Status = status;
        }
    }

    // J2534-1 v04.04 device: open/close plus optional supply voltage.
    internal sealed class J2534Device : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr library);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OpenFn(IntPtr name, out uint deviceId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CloseFn(uint deviceId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IoctlFn(uint handle, uint ioctlId, IntPtr input, out uint output);

        private IntPtr library;
        private CloseFn close;
        private IoctlFn ioctl;
        private uint deviceId;
        private bool opened;

        public string LibraryPath { get; private set; }

        private T GetFunction<T>(string name) where T : class
        {
            IntPtr address = GetProcAddress(library, name);
            if (address == IntPtr.Zero) throw new InvalidOperationException("Driver is missing " + name + ".");
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        public void Open(Adapter adapter)
        {
            if (opened) throw new InvalidOperationException("An adapter is already open.");
            if (!File.Exists(adapter.Library)) throw new FileNotFoundException("J2534 driver not found", adapter.Library);
            library = LoadLibrary(adapter.Library);
            if (library == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not load the 32-bit J2534 driver.");
            try
            {
                var open = GetFunction<OpenFn>("PassThruOpen");
                close = GetFunction<CloseFn>("PassThruClose");
                ioctl = GetFunction<IoctlFn>("PassThruIoctl");
                int status = open(IntPtr.Zero, out deviceId);
                if (status != 0) throw new J2534Exception("PassThruOpen", status);
                opened = true;
                LibraryPath = adapter.Library;
            }
            catch { Dispose(); throw; }
        }

        public double ReadVoltage()
        {
            if (!opened) throw new InvalidOperationException("No adapter is open.");
            uint millivolts;
            int status = ioctl(deviceId, 3, IntPtr.Zero, out millivolts); // READ_VBATT
            if (status != 0) throw new J2534Exception("READ_VBATT", status);
            return millivolts / 1000.0;
        }

        internal IntPtr LibraryHandle { get { return library; } }
        internal uint DeviceHandle { get { return deviceId; } }

        internal void ClearReceiveBuffer()
        {
            if (!opened) throw new InvalidOperationException("No adapter is open.");
            uint ignored;
            int status = ioctl(deviceId, 8, IntPtr.Zero, out ignored); // CLEAR_RX_BUFFER
            if (status != 0) throw new J2534Exception("CLEAR_RX_BUFFER", status);
        }

        public void Dispose()
        {
            if (opened)
            {
                int status = close(deviceId);
                opened = false;
                if (status != 0)
                {
                    library = IntPtr.Zero;
                    throw new InvalidOperationException("PassThruClose failed: J2534 status " + status + ". Restart the bridge before reconnecting.");
                }
            }
            if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
        }
    }

    // Minimal ISO 15765 channel: single-frame requests out, ISO-TP reassembly in.
    internal sealed class J2534Channel : IDisposable
    {
        private const uint ProtocolIso15765 = 6;
        private const uint FilterFlowControl = 3;
        private const uint TxIso15765FramePad = 0x40;
        private const int StatusNoError = 0;
        private const int StatusTimeout = 0x09;
        private const int StatusBufferEmpty = 0x10;
        private const int MaxData = 4128;

        [StructLayout(LayoutKind.Sequential)]
        private struct PassThruMsg
        {
            public uint ProtocolID;
            public uint RxStatus;
            public uint TxFlags;
            public uint Timestamp;
            public uint DataSize;
            public uint ExtraDataIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxData)]
            public byte[] Data;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ConnectFn(uint deviceId, uint protocolId, uint flags, uint baudRate, out uint channelId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DisconnectFn(uint channelId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StartFilterFn(uint channelId, uint filterType, ref PassThruMsg mask,
            ref PassThruMsg pattern, ref PassThruMsg flowControl, out uint filterId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int WriteMsgsFn(uint channelId, [In] PassThruMsg[] messages, ref uint count, uint timeout);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReadMsgsFn(uint channelId, [In, Out] PassThruMsg[] messages, ref uint count, uint timeout);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private readonly J2534Device device;
        private readonly uint requestId;
        private readonly uint responseId;
        private ConnectFn connect;
        private DisconnectFn disconnect;
        private StartFilterFn startFilter;
        private WriteMsgsFn writeMsgs;
        private ReadMsgsFn readMsgs;
        private uint channelId;
        private bool connected;

        internal J2534Channel(J2534Device device, uint requestId, uint responseId, uint baudRate)
        {
            if (device == null) throw new ArgumentNullException("device");
            this.device = device;
            this.requestId = requestId;
            this.responseId = responseId;
            connect = Load<ConnectFn>(device.LibraryHandle, "PassThruConnect");
            disconnect = Load<DisconnectFn>(device.LibraryHandle, "PassThruDisconnect");
            startFilter = Load<StartFilterFn>(device.LibraryHandle, "PassThruStartMsgFilter");
            writeMsgs = Load<WriteMsgsFn>(device.LibraryHandle, "PassThruWriteMsgs");
            readMsgs = Load<ReadMsgsFn>(device.LibraryHandle, "PassThruReadMsgs");

            int status = connect(device.DeviceHandle, ProtocolIso15765, 0, baudRate, out channelId);
            if (status != StatusNoError) throw new J2534Exception("PassThruConnect", status);
            connected = true;
            try { StartFlowControlFilter(); }
            catch { Dispose(); throw; }
        }

        private static T Load<T>(IntPtr library, string name) where T : class
        {
            IntPtr address = GetProcAddress(library, name);
            if (address == IntPtr.Zero) throw new InvalidOperationException("Driver is missing " + name + ".");
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        private static PassThruMsg Message(int dataSize)
        {
            var message = new PassThruMsg { Data = new byte[MaxData] };
            message.ProtocolID = ProtocolIso15765;
            message.DataSize = (uint)dataSize;
            return message;
        }

        private static void SetId(byte[] data, uint id)
        {
            data[0] = (byte)(id >> 24);
            data[1] = (byte)(id >> 16);
            data[2] = (byte)(id >> 8);
            data[3] = (byte)id;
        }

        private void StartFlowControlFilter()
        {
            PassThruMsg mask = Message(4);
            for (int i = 0; i < 4; i++) mask.Data[i] = 0xFF;
            PassThruMsg pattern = Message(4);
            SetId(pattern.Data, responseId);
            PassThruMsg flowControl = Message(7);
            SetId(flowControl.Data, requestId);
            flowControl.Data[4] = 0x30;
            flowControl.Data[5] = 0x00;
            flowControl.Data[6] = 0x00;
            flowControl.TxFlags = TxIso15765FramePad;

            uint filterId;
            int status = startFilter(channelId, FilterFlowControl, ref mask, ref pattern, ref flowControl, out filterId);
            if (status != StatusNoError) throw new J2534Exception("PassThruStartMsgFilter", status);
        }

        // Sends one UDS payload (1-7 bytes) as an ISO-TP single frame.
        internal byte[] Request(byte[] payload, int timeoutMs)
        {
            if (!connected) throw new InvalidOperationException("The ISO 15765 channel is not open.");
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException("timeoutMs");

            byte[] frame = IsoTp.SingleFrame(payload);
            PassThruMsg message = Message(4 + frame.Length);
            SetId(message.Data, requestId);
            Buffer.BlockCopy(frame, 0, message.Data, 4, frame.Length);
            message.TxFlags = TxIso15765FramePad;

            device.ClearReceiveBuffer();
            uint count = 1;
            int status = writeMsgs(channelId, new[] { message }, ref count, (uint)timeoutMs);
            if (status != StatusNoError || count != 1)
                throw new J2534Exception("PassThruWriteMsgs", status == StatusNoError ? -1 : status);

            var reassembler = new IsoTp.Reassembler();
            int deadline = unchecked(Environment.TickCount + timeoutMs);
            while (true)
            {
                int remaining = unchecked(deadline - Environment.TickCount);
                if (remaining <= 0)
                    throw new TimeoutException("No complete ISO-TP response within " + timeoutMs + " ms.");
                byte[] receivedFrame = ReadFrame((uint)Math.Min(remaining, 250));
                if (receivedFrame == null) continue;
                byte[] complete;
                if (reassembler.Add(receivedFrame, out complete)) return complete;
            }
        }

        private byte[] ReadFrame(uint timeoutMs)
        {
            var messages = new PassThruMsg[1];
            messages[0] = Message(0);
            uint count = 1;
            int status = readMsgs(channelId, messages, ref count, timeoutMs);
            if (status == StatusTimeout || status == StatusBufferEmpty || count == 0) return null;
            if (status != StatusNoError) throw new J2534Exception("PassThruReadMsgs", status);

            PassThruMsg message = messages[0];
            if (message.DataSize < 5 || message.DataSize > MaxData) return null;
            uint id = ((uint)message.Data[0] << 24) | ((uint)message.Data[1] << 16)
                | ((uint)message.Data[2] << 8) | message.Data[3];
            if (id != responseId) return null;

            byte[] frame = new byte[message.DataSize - 4];
            Buffer.BlockCopy(message.Data, 4, frame, 0, frame.Length);
            return frame;
        }

        public void Dispose()
        {
            if (connected)
            {
                int status = disconnect(channelId);
                connected = false;
                if (status != StatusNoError) throw new J2534Exception("PassThruDisconnect", status);
            }
        }
    }
}
