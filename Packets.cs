using System;
using System.IO;
using UnityEngine;

namespace MHZombieMultiplayer
{
    public enum PacketType : byte
    {
        HeliState       = 1,
        Chat            = 2,
        RaceFinish      = 3,
        ProjectileState = 4,
        PlayerDamage    = 5,
    }

    public struct HeliStatePacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float Health;
        public bool HasReportedHitbox;
        public Vector3 HitboxWorldCenter;
        public Quaternion HitboxWorldRotation;
        public Vector3 HitboxWorldSize;
    }

    public struct ProjectileStatePacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public int InstanceId;
        public ProjectileKind Kind;
        public float Damage;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float LifeSeconds;
    }

    public struct ChatPacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public string Message;
    }

    public struct RaceFinishPacket
    {
        public PacketType PacketType;
        public ulong SteamId;
        public float TimeSeconds;
    }

    public struct PlayerDamagePacket
    {
        public PacketType PacketType;
        public ulong TargetSteamId;
        public ulong AttackerSteamId;
        public float Damage;
        public int ProjectileInstanceId;
        public float TargetHealthAfter;
    }

    public static class PacketSerializer
    {
        public static PacketType PeekType(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            return (PacketType)data[0];
        }

        // ── HeliState ──────────────────────────────────────────────────────────
        // Base layout is the original 53 bytes. New clients append the target's
        // authoritative effective hitbox [center(12)] [rotation(16)] [size(12)].
        // Old clients ignore the tail; new clients still accept old 53-byte packets.

        public static byte[] Serialize(HeliStatePacket p)
        {
            using (var ms = new MemoryStream(93))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.SteamId);
                WriteVec3(w, p.Position);
                WriteQuat(w, p.Rotation);
                WriteVec3(w, p.Velocity);
                w.Write(p.Health);
                WriteVec3(w, p.HitboxWorldCenter);
                WriteQuat(w, p.HitboxWorldRotation);
                WriteVec3(w, p.HitboxWorldSize);
                return ms.ToArray();
            }
        }

        public static HeliStatePacket DeserializeHeliState(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                var packet = new HeliStatePacket
                {
                    PacketType = (PacketType)r.ReadByte(),
                    SteamId    = r.ReadUInt64(),
                    Position   = ReadVec3(r),
                    Rotation   = ReadQuat(r),
                    Velocity   = ReadVec3(r),
                    Health     = r.ReadSingle(),
                };

                if (ms.Length - ms.Position >= 40)
                {
                    packet.HitboxWorldCenter = ReadVec3(r);
                    packet.HitboxWorldRotation = ReadQuat(r);
                    packet.HitboxWorldSize = ReadVec3(r);
                    packet.HasReportedHitbox = packet.HitboxWorldSize.x > 0f &&
                                               packet.HitboxWorldSize.y > 0f &&
                                               packet.HitboxWorldSize.z > 0f;
                }

                return packet;
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

        // ── RaceFinish ─────────────────────────────────────────────────────────

        public static byte[] Serialize(RaceFinishPacket p)
        {
            using (var ms = new MemoryStream(13))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.SteamId);
                w.Write(p.TimeSeconds);
                return ms.ToArray();
            }
        }

        public static RaceFinishPacket DeserializeRaceFinish(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new RaceFinishPacket
                {
                    PacketType  = (PacketType)r.ReadByte(),
                    SteamId     = r.ReadUInt64(),
                    TimeSeconds = r.ReadSingle(),
                };
            }
        }

        // ── PlayerDamage ───────────────────────────────────────────────────────

        public static byte[] Serialize(PlayerDamagePacket p)
        {
            using (var ms = new MemoryStream(1 + 8 + 8 + 4 + 4 + 4))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.TargetSteamId);
                w.Write(p.AttackerSteamId);
                w.Write(p.Damage);
                w.Write(p.ProjectileInstanceId);
                w.Write(p.TargetHealthAfter);
                return ms.ToArray();
            }
        }

        public static PlayerDamagePacket DeserializePlayerDamage(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new PlayerDamagePacket
                {
                    PacketType = (PacketType)r.ReadByte(),
                    TargetSteamId = r.ReadUInt64(),
                    AttackerSteamId = r.ReadUInt64(),
                    Damage = r.ReadSingle(),
                    ProjectileInstanceId = r.ReadInt32(),
                    TargetHealthAfter = r.ReadSingle(),
                };
            }
        }

        // ── ProjectileState ───────────────────────────────────────────────────

        public static byte[] Serialize(ProjectileStatePacket p)
        {
            using (var ms = new MemoryStream(1 + 8 + 4 + 1 + 4 + 12 + 16 + 12 + 4))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)p.PacketType);
                w.Write(p.SteamId);
                w.Write(p.InstanceId);
                w.Write((byte)p.Kind);
                w.Write(p.Damage);
                WriteVec3(w, p.Position);
                WriteQuat(w, p.Rotation);
                WriteVec3(w, p.Velocity);
                w.Write(p.LifeSeconds);
                return ms.ToArray();
            }
        }

        public static ProjectileStatePacket DeserializeProjectileState(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                return new ProjectileStatePacket
                {
                    PacketType = (PacketType)r.ReadByte(),
                    SteamId = r.ReadUInt64(),
                    InstanceId = r.ReadInt32(),
                    Kind = (ProjectileKind)r.ReadByte(),
                    Damage = r.ReadSingle(),
                    Position = ReadVec3(r),
                    Rotation = ReadQuat(r),
                    Velocity = ReadVec3(r),
                    LifeSeconds = r.ReadSingle(),
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
