using System;
using System.IO;
using UnityEngine;

namespace MHZombieMultiplayer
{
    public enum PacketType : byte
    {
        HeliState = 1,
        Chat      = 2,
    }

    /// <summary>Sent ~20 times/sec: helicopter position, rotation, velocity.</summary>
    public struct HeliStatePacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }

    /// <summary>Reliable chat message.</summary>
    public struct ChatPacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public string Message;
    }

    public static class PacketSerializer
    {
        public static PacketType PeekType(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            return (PacketType)data[0];
        }

        // ── HeliState ──────────────────────────────────────────────────────────
        // Layout: [type(1)] [steamId(8)] [pos(12)] [rot(16)] [vel(12)] = 49 bytes

        public static byte[] Serialize(HeliStatePacket p)
        {
            using (var ms = new MemoryStream(49))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.SteamId);
                WriteVec3(w, p.Position);
                WriteQuat(w, p.Rotation);
                WriteVec3(w, p.Velocity);
                return ms.ToArray();
            }
        }

        public static HeliStatePacket DeserializeHeliState(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new HeliStatePacket
                {
                    PacketType = (PacketType)r.ReadByte(),
                    SteamId    = r.ReadUInt64(),
                    Position   = ReadVec3(r),
                    Rotation   = ReadQuat(r),
                    Velocity   = ReadVec3(r),
                };
            }
        }

        // ── Chat ───────────────────────────────────────────────────────────────

        public static byte[] Serialize(ChatPacket p)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.SteamId);
                w.Write(p.Message ?? string.Empty);
                return ms.ToArray();
            }
        }

        public static ChatPacket DeserializeChat(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new ChatPacket
                {
                    PacketType = (PacketType)r.ReadByte(),
                    SteamId    = r.ReadUInt64(),
                    Message    = r.ReadString(),
                };
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void WriteVec3(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x); w.Write(v.y); w.Write(v.z);
        }

        private static Vector3 ReadVec3(BinaryReader r)
            => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteQuat(BinaryWriter w, Quaternion q)
        {
            w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
        }

        private static Quaternion ReadQuat(BinaryReader r)
            => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
