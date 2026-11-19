// Hallow Hub v1.2 - Xbox (XInput) controller. Read only while Hub's window is in front, so it never reacts during a game.
using System;
using System.Runtime.InteropServices;

namespace HallowHub
{
    public class Pad
    {
        [StructLayout(LayoutKind.Sequential)] struct Gamepad { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        [StructLayout(LayoutKind.Sequential)] struct State { public uint Packet; public Gamepad Pad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int Get14(int i, out State s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int i, out State s);

        public const ushort Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020,
            LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
        int dll;   // 0 = try xinput1_4, 1 = xinput9_1_0, 2 = none
        ushort last; DateTime held = DateTime.MaxValue, repeat;
        public bool Connected;

        // Returns the buttons newly pressed (or repeated while a direction is held), with the left stick turned into the d-pad.
        public ushort Poll()
        {
            State s = new State(); int r;
            try { r = dll == 0 ? Get14(0, out s) : dll == 1 ? Get910(0, out s) : -1; }
            catch (DllNotFoundException) { dll++; return 0; }
            catch (EntryPointNotFoundException) { dll++; return 0; }
            if (r != 0) { Connected = false; last = 0; return 0; }
            Connected = true;
            ushort b = s.Pad.Buttons;
            const short dead = 16000;
            if (s.Pad.LY > dead) b |= Up; if (s.Pad.LY < -dead) b |= Down; if (s.Pad.LX < -dead) b |= Left; if (s.Pad.LX > dead) b |= Right;
            ushort pressed = (ushort)(b & ~last);
            const ushort dirs = Up | Down | Left | Right;
            var now = DateTime.UtcNow;
            if ((pressed & dirs) != 0) { held = now.AddMilliseconds(380); }
            else if ((b & dirs) != 0 && now >= held && now >= repeat) { pressed |= (ushort)(b & dirs); repeat = now.AddMilliseconds(140); }
            if ((b & dirs) == 0) held = DateTime.MaxValue;
            last = b;
            return pressed;
        }
    }
}
