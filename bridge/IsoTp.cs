using System;
using System.IO;

namespace P3Orbit
{
    // ISO 15765-2 framing. Pure logic; no driver, no vehicle.
    // Carried over from the P3ToolClone investigation so behaviour matches there.
    internal static class IsoTp
    {
        internal const int MaxMessageLength = 4096;

        internal static byte[] SingleFrame(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (payload.Length == 0 || payload.Length > 7)
                throw new ArgumentOutOfRangeException("payload", "A single frame carries 1-7 bytes.");
            byte[] frame = new byte[payload.Length + 1];
            frame[0] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 1, payload.Length);
            return frame;
        }

        internal sealed class Reassembler
        {
            private byte[] message;
            private int received;
            private byte nextSequence;
            private bool active;

            internal bool Active { get { return active; } }

            internal void Reset()
            {
                message = null;
                received = 0;
                nextSequence = 1;
                active = false;
            }

            // Returns true when a complete diagnostic message is available.
            internal bool Add(byte[] frame, out byte[] complete)
            {
                complete = null;
                if (frame == null || frame.Length == 0)
                    throw new InvalidDataException("Empty ISO-TP frame.");

                int type = frame[0] >> 4;
                if (type == 0)
                {
                    int length = frame[0] & 0x0F;
                    if (length == 0 || length > 7 || frame.Length < length + 1)
                        throw new InvalidDataException("Invalid ISO-TP single frame length.");
                    complete = new byte[length];
                    Buffer.BlockCopy(frame, 1, complete, 0, length);
                    Reset();
                    return true;
                }

                if (type == 1)
                {
                    if (frame.Length < 8)
                        throw new InvalidDataException("Truncated ISO-TP first frame.");
                    int total = ((frame[0] & 0x0F) << 8) | frame[1];
                    if (total <= 7 || total > MaxMessageLength)
                        throw new InvalidDataException("Invalid ISO-TP message length.");
                    message = new byte[total];
                    Buffer.BlockCopy(frame, 2, message, 0, 6);
                    received = 6;
                    nextSequence = 1;
                    active = true;
                    return false;
                }

                if (type == 2)
                {
                    if (!active || message == null)
                        throw new InvalidDataException("ISO-TP consecutive frame without a first frame.");
                    byte sequence = (byte)(frame[0] & 0x0F);
                    if (sequence != nextSequence)
                        throw new InvalidDataException("ISO-TP sequence mismatch.");
                    int count = Math.Min(frame.Length - 1, message.Length - received);
                    if (count <= 0)
                        throw new InvalidDataException("ISO-TP message already complete.");
                    Buffer.BlockCopy(frame, 1, message, received, count);
                    received += count;
                    nextSequence = (byte)((nextSequence + 1) & 0x0F);
                    if (received >= message.Length)
                    {
                        complete = message;
                        Reset();
                        return true;
                    }
                    return false;
                }

                if (type == 3)
                    throw new InvalidDataException("Unexpected ISO-TP flow-control frame.");

                throw new InvalidDataException("Unsupported ISO-TP frame type.");
            }
        }
    }
}
