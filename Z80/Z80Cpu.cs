using System;
using System.Collections.Generic;
using System.Text;

namespace Spectrum128kEmulator.Z80
{
    public class Z80Cpu
    {
        public Z80Registers Regs { get; } = new Z80Registers();
        public ulong TStates { get; private set; } = 0;

        public Func<ushort, byte> ReadMemory { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WriteMemory { get; set; } = (_, _) => { };
        public Func<ushort, byte> ReadPort { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };
        public Action<string>? Trace { get; set; }
        public Func<Z80Cpu, bool>? BeforeInstruction { get; set; }

        private bool halted = false;
        public bool IsHalted => halted;
        public bool InterruptPending { get; set; } = false;
        public bool IFF1 { get; private set; } = false;
        public bool IFF2 { get; private set; } = false;

        private int eiDelay = 0;
        private int interruptMode = 1;
        private byte qFlags = 0;
        private readonly Action[] opcodeTable = new Action[256];
        private readonly Action[] cbOpcodeTable = new Action[256];
        private readonly Action[] edOpcodeTable = new Action[256];
        private readonly Action[] ddOpcodeTable = new Action[256];
        private readonly Action[] fdOpcodeTable = new Action[256];

        private readonly Queue<string> recentTrace = new Queue<string>();
        private bool reportedHighRamEntry = false;
        private bool flagsChangedLastInstruction = false;
        private byte lastFlagsBeforeInstruction = 0;

        private enum Flag : byte
        {
            C = 0,
            N = 1,
            P = 2,
            F3 = 3,
            H = 4,
            F5 = 5,
            Z = 6,
            S = 7
        }

        private static readonly ushort[] DaaAfTable = new ushort[]
        {
            0x0044, 0x0100, 0x0200, 0x0304, 0x0400, 0x0504, 0x0604, 0x0700, 0x0808, 0x090C, 0x1010, 0x1114, 0x1214, 0x1310, 0x1414, 0x1510,
            0x1000, 0x1104, 0x1204, 0x1300, 0x1404, 0x1500, 0x1600, 0x1704, 0x180C, 0x1908, 0x2030, 0x2134, 0x2234, 0x2330, 0x2434, 0x2530,
            0x2020, 0x2124, 0x2224, 0x2320, 0x2424, 0x2520, 0x2620, 0x2724, 0x282C, 0x2928, 0x3034, 0x3130, 0x3230, 0x3334, 0x3430, 0x3534,
            0x3024, 0x3120, 0x3220, 0x3324, 0x3420, 0x3524, 0x3624, 0x3720, 0x3828, 0x392C, 0x4010, 0x4114, 0x4214, 0x4310, 0x4414, 0x4510,
            0x4000, 0x4104, 0x4204, 0x4300, 0x4404, 0x4500, 0x4600, 0x4704, 0x480C, 0x4908, 0x5014, 0x5110, 0x5210, 0x5314, 0x5410, 0x5514,
            0x5004, 0x5100, 0x5200, 0x5304, 0x5400, 0x5504, 0x5604, 0x5700, 0x5808, 0x590C, 0x6034, 0x6130, 0x6230, 0x6334, 0x6430, 0x6534,
            0x6024, 0x6120, 0x6220, 0x6324, 0x6420, 0x6524, 0x6624, 0x6720, 0x6828, 0x692C, 0x7030, 0x7134, 0x7234, 0x7330, 0x7434, 0x7530,
            0x7020, 0x7124, 0x7224, 0x7320, 0x7424, 0x7520, 0x7620, 0x7724, 0x782C, 0x7928, 0x8090, 0x8194, 0x8294, 0x8390, 0x8494, 0x8590,
            0x8080, 0x8184, 0x8284, 0x8380, 0x8484, 0x8580, 0x8680, 0x8784, 0x888C, 0x8988, 0x9094, 0x9190, 0x9290, 0x9394, 0x9490, 0x9594,
            0x9084, 0x9180, 0x9280, 0x9384, 0x9480, 0x9584, 0x9684, 0x9780, 0x9888, 0x998C, 0x0055, 0x0111, 0x0211, 0x0315, 0x0411, 0x0515,
            0x0045, 0x0101, 0x0201, 0x0305, 0x0401, 0x0505, 0x0605, 0x0701, 0x0809, 0x090D, 0x1011, 0x1115, 0x1215, 0x1311, 0x1415, 0x1511,
            0x1001, 0x1105, 0x1205, 0x1301, 0x1405, 0x1501, 0x1601, 0x1705, 0x180D, 0x1909, 0x2031, 0x2135, 0x2235, 0x2331, 0x2435, 0x2531,
            0x2021, 0x2125, 0x2225, 0x2321, 0x2425, 0x2521, 0x2621, 0x2725, 0x282D, 0x2929, 0x3035, 0x3131, 0x3231, 0x3335, 0x3431, 0x3535,
            0x3025, 0x3121, 0x3221, 0x3325, 0x3421, 0x3525, 0x3625, 0x3721, 0x3829, 0x392D, 0x4011, 0x4115, 0x4215, 0x4311, 0x4415, 0x4511,
            0x4001, 0x4105, 0x4205, 0x4301, 0x4405, 0x4501, 0x4601, 0x4705, 0x480D, 0x4909, 0x5015, 0x5111, 0x5211, 0x5315, 0x5411, 0x5515,
            0x5005, 0x5101, 0x5201, 0x5305, 0x5401, 0x5505, 0x5605, 0x5701, 0x5809, 0x590D, 0x6035, 0x6131, 0x6231, 0x6335, 0x6431, 0x6535,
            0x6025, 0x6121, 0x6221, 0x6325, 0x6421, 0x6525, 0x6625, 0x6721, 0x6829, 0x692D, 0x7031, 0x7135, 0x7235, 0x7331, 0x7435, 0x7531,
            0x7021, 0x7125, 0x7225, 0x7321, 0x7425, 0x7521, 0x7621, 0x7725, 0x782D, 0x7929, 0x8091, 0x8195, 0x8295, 0x8391, 0x8495, 0x8591,
            0x8081, 0x8185, 0x8285, 0x8381, 0x8485, 0x8581, 0x8681, 0x8785, 0x888D, 0x8989, 0x9095, 0x9191, 0x9291, 0x9395, 0x9491, 0x9595,
            0x9085, 0x9181, 0x9281, 0x9385, 0x9481, 0x9585, 0x9685, 0x9781, 0x9889, 0x998D, 0xA0B5, 0xA1B1, 0xA2B1, 0xA3B5, 0xA4B1, 0xA5B5,
            0xA0A5, 0xA1A1, 0xA2A1, 0xA3A5, 0xA4A1, 0xA5A5, 0xA6A5, 0xA7A1, 0xA8A9, 0xA9AD, 0xB0B1, 0xB1B5, 0xB2B5, 0xB3B1, 0xB4B5, 0xB5B1,
            0xB0A1, 0xB1A5, 0xB2A5, 0xB3A1, 0xB4A5, 0xB5A1, 0xB6A1, 0xB7A5, 0xB8AD, 0xB9A9, 0xC095, 0xC191, 0xC291, 0xC395, 0xC491, 0xC595,
            0xC085, 0xC181, 0xC281, 0xC385, 0xC481, 0xC585, 0xC685, 0xC781, 0xC889, 0xC98D, 0xD091, 0xD195, 0xD295, 0xD391, 0xD495, 0xD591,
            0xD081, 0xD185, 0xD285, 0xD381, 0xD485, 0xD581, 0xD681, 0xD785, 0xD88D, 0xD989, 0xE0B1, 0xE1B5, 0xE2B5, 0xE3B1, 0xE4B5, 0xE5B1,
            0xE0A1, 0xE1A5, 0xE2A5, 0xE3A1, 0xE4A5, 0xE5A1, 0xE6A1, 0xE7A5, 0xE8AD, 0xE9A9, 0xF0B5, 0xF1B1, 0xF2B1, 0xF3B5, 0xF4B1, 0xF5B5,
            0xF0A5, 0xF1A1, 0xF2A1, 0xF3A5, 0xF4A1, 0xF5A5, 0xF6A5, 0xF7A1, 0xF8A9, 0xF9AD, 0x0055, 0x0111, 0x0211, 0x0315, 0x0411, 0x0515,
            0x0045, 0x0101, 0x0201, 0x0305, 0x0401, 0x0505, 0x0605, 0x0701, 0x0809, 0x090D, 0x1011, 0x1115, 0x1215, 0x1311, 0x1415, 0x1511,
            0x1001, 0x1105, 0x1205, 0x1301, 0x1405, 0x1501, 0x1601, 0x1705, 0x180D, 0x1909, 0x2031, 0x2135, 0x2235, 0x2331, 0x2435, 0x2531,
            0x2021, 0x2125, 0x2225, 0x2321, 0x2425, 0x2521, 0x2621, 0x2725, 0x282D, 0x2929, 0x3035, 0x3131, 0x3231, 0x3335, 0x3431, 0x3535,
            0x3025, 0x3121, 0x3221, 0x3325, 0x3421, 0x3525, 0x3625, 0x3721, 0x3829, 0x392D, 0x4011, 0x4115, 0x4215, 0x4311, 0x4415, 0x4511,
            0x4001, 0x4105, 0x4205, 0x4301, 0x4405, 0x4501, 0x4601, 0x4705, 0x480D, 0x4909, 0x5015, 0x5111, 0x5211, 0x5315, 0x5411, 0x5515,
            0x5005, 0x5101, 0x5201, 0x5305, 0x5401, 0x5505, 0x5605, 0x5701, 0x5809, 0x590D, 0x6035, 0x6131, 0x6231, 0x6335, 0x6431, 0x6535,
            0x0046, 0x0102, 0x0202, 0x0306, 0x0402, 0x0506, 0x0606, 0x0702, 0x080A, 0x090E, 0x0402, 0x0506, 0x0606, 0x0702, 0x080A, 0x090E,
            0x1002, 0x1106, 0x1206, 0x1302, 0x1406, 0x1502, 0x1602, 0x1706, 0x180E, 0x190A, 0x1406, 0x1502, 0x1602, 0x1706, 0x180E, 0x190A,
            0x2022, 0x2126, 0x2226, 0x2322, 0x2426, 0x2522, 0x2622, 0x2726, 0x282E, 0x292A, 0x2426, 0x2522, 0x2622, 0x2726, 0x282E, 0x292A,
            0x3026, 0x3122, 0x3222, 0x3326, 0x3422, 0x3526, 0x3626, 0x3722, 0x382A, 0x392E, 0x3422, 0x3526, 0x3626, 0x3722, 0x382A, 0x392E,
            0x4002, 0x4106, 0x4206, 0x4302, 0x4406, 0x4502, 0x4602, 0x4706, 0x480E, 0x490A, 0x4406, 0x4502, 0x4602, 0x4706, 0x480E, 0x490A,
            0x5006, 0x5102, 0x5202, 0x5306, 0x5402, 0x5506, 0x5606, 0x5702, 0x580A, 0x590E, 0x5402, 0x5506, 0x5606, 0x5702, 0x580A, 0x590E,
            0x6026, 0x6122, 0x6222, 0x6326, 0x6422, 0x6526, 0x6626, 0x6722, 0x682A, 0x692E, 0x6422, 0x6526, 0x6626, 0x6722, 0x682A, 0x692E,
            0x7022, 0x7126, 0x7226, 0x7322, 0x7426, 0x7522, 0x7622, 0x7726, 0x782E, 0x792A, 0x7426, 0x7522, 0x7622, 0x7726, 0x782E, 0x792A,
            0x8082, 0x8186, 0x8286, 0x8382, 0x8486, 0x8582, 0x8682, 0x8786, 0x888E, 0x898A, 0x8486, 0x8582, 0x8682, 0x8786, 0x888E, 0x898A,
            0x9086, 0x9182, 0x9282, 0x9386, 0x9482, 0x9586, 0x9686, 0x9782, 0x988A, 0x998E, 0x3423, 0x3527, 0x3627, 0x3723, 0x382B, 0x392F,
            0x4003, 0x4107, 0x4207, 0x4303, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B,
            0x5007, 0x5103, 0x5203, 0x5307, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F,
            0x6027, 0x6123, 0x6223, 0x6327, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F,
            0x7023, 0x7127, 0x7227, 0x7323, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B,
            0x8083, 0x8187, 0x8287, 0x8383, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B,
            0x9087, 0x9183, 0x9283, 0x9387, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F,
            0xA0A7, 0xA1A3, 0xA2A3, 0xA3A7, 0xA4A3, 0xA5A7, 0xA6A7, 0xA7A3, 0xA8AB, 0xA9AF, 0xA4A3, 0xA5A7, 0xA6A7, 0xA7A3, 0xA8AB, 0xA9AF,
            0xB0A3, 0xB1A7, 0xB2A7, 0xB3A3, 0xB4A7, 0xB5A3, 0xB6A3, 0xB7A7, 0xB8AF, 0xB9AB, 0xB4A7, 0xB5A3, 0xB6A3, 0xB7A7, 0xB8AF, 0xB9AB,
            0xC087, 0xC183, 0xC283, 0xC387, 0xC483, 0xC587, 0xC687, 0xC783, 0xC88B, 0xC98F, 0xC483, 0xC587, 0xC687, 0xC783, 0xC88B, 0xC98F,
            0xD083, 0xD187, 0xD287, 0xD383, 0xD487, 0xD583, 0xD683, 0xD787, 0xD88F, 0xD98B, 0xD487, 0xD583, 0xD683, 0xD787, 0xD88F, 0xD98B,
            0xE0A3, 0xE1A7, 0xE2A7, 0xE3A3, 0xE4A7, 0xE5A3, 0xE6A3, 0xE7A7, 0xE8AF, 0xE9AB, 0xE4A7, 0xE5A3, 0xE6A3, 0xE7A7, 0xE8AF, 0xE9AB,
            0xF0A7, 0xF1A3, 0xF2A3, 0xF3A7, 0xF4A3, 0xF5A7, 0xF6A7, 0xF7A3, 0xF8AB, 0xF9AF, 0xF4A3, 0xF5A7, 0xF6A7, 0xF7A3, 0xF8AB, 0xF9AF,
            0x0047, 0x0103, 0x0203, 0x0307, 0x0403, 0x0507, 0x0607, 0x0703, 0x080B, 0x090F, 0x0403, 0x0507, 0x0607, 0x0703, 0x080B, 0x090F,
            0x1003, 0x1107, 0x1207, 0x1303, 0x1407, 0x1503, 0x1603, 0x1707, 0x180F, 0x190B, 0x1407, 0x1503, 0x1603, 0x1707, 0x180F, 0x190B,
            0x2023, 0x2127, 0x2227, 0x2323, 0x2427, 0x2523, 0x2623, 0x2727, 0x282F, 0x292B, 0x2427, 0x2523, 0x2623, 0x2727, 0x282F, 0x292B,
            0x3027, 0x3123, 0x3223, 0x3327, 0x3423, 0x3527, 0x3627, 0x3723, 0x382B, 0x392F, 0x3423, 0x3527, 0x3627, 0x3723, 0x382B, 0x392F,
            0x4003, 0x4107, 0x4207, 0x4303, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B,
            0x5007, 0x5103, 0x5203, 0x5307, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F,
            0x6027, 0x6123, 0x6223, 0x6327, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F,
            0x7023, 0x7127, 0x7227, 0x7323, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B,
            0x8083, 0x8187, 0x8287, 0x8383, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B,
            0x9087, 0x9183, 0x9283, 0x9387, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F,
            0x0604, 0x0700, 0x0808, 0x090C, 0x0A0C, 0x0B08, 0x0C0C, 0x0D08, 0x0E08, 0x0F0C, 0x1010, 0x1114, 0x1214, 0x1310, 0x1414, 0x1510,
            0x1600, 0x1704, 0x180C, 0x1908, 0x1A08, 0x1B0C, 0x1C08, 0x1D0C, 0x1E0C, 0x1F08, 0x2030, 0x2134, 0x2234, 0x2330, 0x2434, 0x2530,
            0x2620, 0x2724, 0x282C, 0x2928, 0x2A28, 0x2B2C, 0x2C28, 0x2D2C, 0x2E2C, 0x2F28, 0x3034, 0x3130, 0x3230, 0x3334, 0x3430, 0x3534,
            0x3624, 0x3720, 0x3828, 0x392C, 0x3A2C, 0x3B28, 0x3C2C, 0x3D28, 0x3E28, 0x3F2C, 0x4010, 0x4114, 0x4214, 0x4310, 0x4414, 0x4510,
            0x4600, 0x4704, 0x480C, 0x4908, 0x4A08, 0x4B0C, 0x4C08, 0x4D0C, 0x4E0C, 0x4F08, 0x5014, 0x5110, 0x5210, 0x5314, 0x5410, 0x5514,
            0x5604, 0x5700, 0x5808, 0x590C, 0x5A0C, 0x5B08, 0x5C0C, 0x5D08, 0x5E08, 0x5F0C, 0x6034, 0x6130, 0x6230, 0x6334, 0x6430, 0x6534,
            0x6624, 0x6720, 0x6828, 0x692C, 0x6A2C, 0x6B28, 0x6C2C, 0x6D28, 0x6E28, 0x6F2C, 0x7030, 0x7134, 0x7234, 0x7330, 0x7434, 0x7530,
            0x7620, 0x7724, 0x782C, 0x7928, 0x7A28, 0x7B2C, 0x7C28, 0x7D2C, 0x7E2C, 0x7F28, 0x8090, 0x8194, 0x8294, 0x8390, 0x8494, 0x8590,
            0x8680, 0x8784, 0x888C, 0x8988, 0x8A88, 0x8B8C, 0x8C88, 0x8D8C, 0x8E8C, 0x8F88, 0x9094, 0x9190, 0x9290, 0x9394, 0x9490, 0x9594,
            0x9684, 0x9780, 0x9888, 0x998C, 0x9A8C, 0x9B88, 0x9C8C, 0x9D88, 0x9E88, 0x9F8C, 0x0055, 0x0111, 0x0211, 0x0315, 0x0411, 0x0515,
            0x0605, 0x0701, 0x0809, 0x090D, 0x0A0D, 0x0B09, 0x0C0D, 0x0D09, 0x0E09, 0x0F0D, 0x1011, 0x1115, 0x1215, 0x1311, 0x1415, 0x1511,
            0x1601, 0x1705, 0x180D, 0x1909, 0x1A09, 0x1B0D, 0x1C09, 0x1D0D, 0x1E0D, 0x1F09, 0x2031, 0x2135, 0x2235, 0x2331, 0x2435, 0x2531,
            0x2621, 0x2725, 0x282D, 0x2929, 0x2A29, 0x2B2D, 0x2C29, 0x2D2D, 0x2E2D, 0x2F29, 0x3035, 0x3131, 0x3231, 0x3335, 0x3431, 0x3535,
            0x3625, 0x3721, 0x3829, 0x392D, 0x3A2D, 0x3B29, 0x3C2D, 0x3D29, 0x3E29, 0x3F2D, 0x4011, 0x4115, 0x4215, 0x4311, 0x4415, 0x4511,
            0x4601, 0x4705, 0x480D, 0x4909, 0x4A09, 0x4B0D, 0x4C09, 0x4D0D, 0x4E0D, 0x4F09, 0x5015, 0x5111, 0x5211, 0x5315, 0x5411, 0x5515,
            0x5605, 0x5701, 0x5809, 0x590D, 0x5A0D, 0x5B09, 0x5C0D, 0x5D09, 0x5E09, 0x5F0D, 0x6035, 0x6131, 0x6231, 0x6335, 0x6431, 0x6535,
            0x6625, 0x6721, 0x6829, 0x692D, 0x6A2D, 0x6B29, 0x6C2D, 0x6D29, 0x6E29, 0x6F2D, 0x7031, 0x7135, 0x7235, 0x7331, 0x7435, 0x7531,
            0x7621, 0x7725, 0x782D, 0x7929, 0x7A29, 0x7B2D, 0x7C29, 0x7D2D, 0x7E2D, 0x7F29, 0x8091, 0x8195, 0x8295, 0x8391, 0x8495, 0x8591,
            0x8681, 0x8785, 0x888D, 0x8989, 0x8A89, 0x8B8D, 0x8C89, 0x8D8D, 0x8E8D, 0x8F89, 0x9095, 0x9191, 0x9291, 0x9395, 0x9491, 0x9595,
            0x9685, 0x9781, 0x9889, 0x998D, 0x9A8D, 0x9B89, 0x9C8D, 0x9D89, 0x9E89, 0x9F8D, 0xA0B5, 0xA1B1, 0xA2B1, 0xA3B5, 0xA4B1, 0xA5B5,
            0xA6A5, 0xA7A1, 0xA8A9, 0xA9AD, 0xAAAD, 0xABA9, 0xACAD, 0xADA9, 0xAEA9, 0xAFAD, 0xB0B1, 0xB1B5, 0xB2B5, 0xB3B1, 0xB4B5, 0xB5B1,
            0xB6A1, 0xB7A5, 0xB8AD, 0xB9A9, 0xBAA9, 0xBBAD, 0xBCA9, 0xBDAD, 0xBEAD, 0xBFA9, 0xC095, 0xC191, 0xC291, 0xC395, 0xC491, 0xC595,
            0xC685, 0xC781, 0xC889, 0xC98D, 0xCA8D, 0xCB89, 0xCC8D, 0xCD89, 0xCE89, 0xCF8D, 0xD091, 0xD195, 0xD295, 0xD391, 0xD495, 0xD591,
            0xD681, 0xD785, 0xD88D, 0xD989, 0xDA89, 0xDB8D, 0xDC89, 0xDD8D, 0xDE8D, 0xDF89, 0xE0B1, 0xE1B5, 0xE2B5, 0xE3B1, 0xE4B5, 0xE5B1,
            0xE6A1, 0xE7A5, 0xE8AD, 0xE9A9, 0xEAA9, 0xEBAD, 0xECA9, 0xEDAD, 0xEEAD, 0xEFA9, 0xF0B5, 0xF1B1, 0xF2B1, 0xF3B5, 0xF4B1, 0xF5B5,
            0xF6A5, 0xF7A1, 0xF8A9, 0xF9AD, 0xFAAD, 0xFBA9, 0xFCAD, 0xFDA9, 0xFEA9, 0xFFAD, 0x0055, 0x0111, 0x0211, 0x0315, 0x0411, 0x0515,
            0x0605, 0x0701, 0x0809, 0x090D, 0x0A0D, 0x0B09, 0x0C0D, 0x0D09, 0x0E09, 0x0F0D, 0x1011, 0x1115, 0x1215, 0x1311, 0x1415, 0x1511,
            0x1601, 0x1705, 0x180D, 0x1909, 0x1A09, 0x1B0D, 0x1C09, 0x1D0D, 0x1E0D, 0x1F09, 0x2031, 0x2135, 0x2235, 0x2331, 0x2435, 0x2531,
            0x2621, 0x2725, 0x282D, 0x2929, 0x2A29, 0x2B2D, 0x2C29, 0x2D2D, 0x2E2D, 0x2F29, 0x3035, 0x3131, 0x3231, 0x3335, 0x3431, 0x3535,
            0x3625, 0x3721, 0x3829, 0x392D, 0x3A2D, 0x3B29, 0x3C2D, 0x3D29, 0x3E29, 0x3F2D, 0x4011, 0x4115, 0x4215, 0x4311, 0x4415, 0x4511,
            0x4601, 0x4705, 0x480D, 0x4909, 0x4A09, 0x4B0D, 0x4C09, 0x4D0D, 0x4E0D, 0x4F09, 0x5015, 0x5111, 0x5211, 0x5315, 0x5411, 0x5515,
            0x5605, 0x5701, 0x5809, 0x590D, 0x5A0D, 0x5B09, 0x5C0D, 0x5D09, 0x5E09, 0x5F0D, 0x6035, 0x6131, 0x6231, 0x6335, 0x6431, 0x6535,
            0xFABE, 0xFBBA, 0xFCBE, 0xFDBA, 0xFEBA, 0xFFBE, 0x0046, 0x0102, 0x0202, 0x0306, 0x0402, 0x0506, 0x0606, 0x0702, 0x080A, 0x090E,
            0x0A1E, 0x0B1A, 0x0C1E, 0x0D1A, 0x0E1A, 0x0F1E, 0x1002, 0x1106, 0x1206, 0x1302, 0x1406, 0x1502, 0x1602, 0x1706, 0x180E, 0x190A,
            0x1A1A, 0x1B1E, 0x1C1A, 0x1D1E, 0x1E1E, 0x1F1A, 0x2022, 0x2126, 0x2226, 0x2322, 0x2426, 0x2522, 0x2622, 0x2726, 0x282E, 0x292A,
            0x2A3A, 0x2B3E, 0x2C3A, 0x2D3E, 0x2E3E, 0x2F3A, 0x3026, 0x3122, 0x3222, 0x3326, 0x3422, 0x3526, 0x3626, 0x3722, 0x382A, 0x392E,
            0x3A3E, 0x3B3A, 0x3C3E, 0x3D3A, 0x3E3A, 0x3F3E, 0x4002, 0x4106, 0x4206, 0x4302, 0x4406, 0x4502, 0x4602, 0x4706, 0x480E, 0x490A,
            0x4A1A, 0x4B1E, 0x4C1A, 0x4D1E, 0x4E1E, 0x4F1A, 0x5006, 0x5102, 0x5202, 0x5306, 0x5402, 0x5506, 0x5606, 0x5702, 0x580A, 0x590E,
            0x5A1E, 0x5B1A, 0x5C1E, 0x5D1A, 0x5E1A, 0x5F1E, 0x6026, 0x6122, 0x6222, 0x6326, 0x6422, 0x6526, 0x6626, 0x6722, 0x682A, 0x692E,
            0x6A3E, 0x6B3A, 0x6C3E, 0x6D3A, 0x6E3A, 0x6F3E, 0x7022, 0x7126, 0x7226, 0x7322, 0x7426, 0x7522, 0x7622, 0x7726, 0x782E, 0x792A,
            0x7A3A, 0x7B3E, 0x7C3A, 0x7D3E, 0x7E3E, 0x7F3A, 0x8082, 0x8186, 0x8286, 0x8382, 0x8486, 0x8582, 0x8682, 0x8786, 0x888E, 0x898A,
            0x8A9A, 0x8B9E, 0x8C9A, 0x8D9E, 0x8E9E, 0x8F9A, 0x9086, 0x9182, 0x9282, 0x9386, 0x3423, 0x3527, 0x3627, 0x3723, 0x382B, 0x392F,
            0x3A3F, 0x3B3B, 0x3C3F, 0x3D3B, 0x3E3B, 0x3F3F, 0x4003, 0x4107, 0x4207, 0x4303, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B,
            0x4A1B, 0x4B1F, 0x4C1B, 0x4D1F, 0x4E1F, 0x4F1B, 0x5007, 0x5103, 0x5203, 0x5307, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F,
            0x5A1F, 0x5B1B, 0x5C1F, 0x5D1B, 0x5E1B, 0x5F1F, 0x6027, 0x6123, 0x6223, 0x6327, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F,
            0x6A3F, 0x6B3B, 0x6C3F, 0x6D3B, 0x6E3B, 0x6F3F, 0x7023, 0x7127, 0x7227, 0x7323, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B,
            0x7A3B, 0x7B3F, 0x7C3B, 0x7D3F, 0x7E3F, 0x7F3B, 0x8083, 0x8187, 0x8287, 0x8383, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B,
            0x8A9B, 0x8B9F, 0x8C9B, 0x8D9F, 0x8E9F, 0x8F9B, 0x9087, 0x9183, 0x9283, 0x9387, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F,
            0x9A9F, 0x9B9B, 0x9C9F, 0x9D9B, 0x9E9B, 0x9F9F, 0xA0A7, 0xA1A3, 0xA2A3, 0xA3A7, 0xA4A3, 0xA5A7, 0xA6A7, 0xA7A3, 0xA8AB, 0xA9AF,
            0xAABF, 0xABBB, 0xACBF, 0xADBB, 0xAEBB, 0xAFBF, 0xB0A3, 0xB1A7, 0xB2A7, 0xB3A3, 0xB4A7, 0xB5A3, 0xB6A3, 0xB7A7, 0xB8AF, 0xB9AB,
            0xBABB, 0xBBBF, 0xBCBB, 0xBDBF, 0xBEBF, 0xBFBB, 0xC087, 0xC183, 0xC283, 0xC387, 0xC483, 0xC587, 0xC687, 0xC783, 0xC88B, 0xC98F,
            0xCA9F, 0xCB9B, 0xCC9F, 0xCD9B, 0xCE9B, 0xCF9F, 0xD083, 0xD187, 0xD287, 0xD383, 0xD487, 0xD583, 0xD683, 0xD787, 0xD88F, 0xD98B,
            0xDA9B, 0xDB9F, 0xDC9B, 0xDD9F, 0xDE9F, 0xDF9B, 0xE0A3, 0xE1A7, 0xE2A7, 0xE3A3, 0xE4A7, 0xE5A3, 0xE6A3, 0xE7A7, 0xE8AF, 0xE9AB,
            0xEABB, 0xEBBF, 0xECBB, 0xEDBF, 0xEEBF, 0xEFBB, 0xF0A7, 0xF1A3, 0xF2A3, 0xF3A7, 0xF4A3, 0xF5A7, 0xF6A7, 0xF7A3, 0xF8AB, 0xF9AF,
            0xFABF, 0xFBBB, 0xFCBF, 0xFDBB, 0xFEBB, 0xFFBF, 0x0047, 0x0103, 0x0203, 0x0307, 0x0403, 0x0507, 0x0607, 0x0703, 0x080B, 0x090F,
            0x0A1F, 0x0B1B, 0x0C1F, 0x0D1B, 0x0E1B, 0x0F1F, 0x1003, 0x1107, 0x1207, 0x1303, 0x1407, 0x1503, 0x1603, 0x1707, 0x180F, 0x190B,
            0x1A1B, 0x1B1F, 0x1C1B, 0x1D1F, 0x1E1F, 0x1F1B, 0x2023, 0x2127, 0x2227, 0x2323, 0x2427, 0x2523, 0x2623, 0x2727, 0x282F, 0x292B,
            0x2A3B, 0x2B3F, 0x2C3B, 0x2D3F, 0x2E3F, 0x2F3B, 0x3027, 0x3123, 0x3223, 0x3327, 0x3423, 0x3527, 0x3627, 0x3723, 0x382B, 0x392F,
            0x3A3F, 0x3B3B, 0x3C3F, 0x3D3B, 0x3E3B, 0x3F3F, 0x4003, 0x4107, 0x4207, 0x4303, 0x4407, 0x4503, 0x4603, 0x4707, 0x480F, 0x490B,
            0x4A1B, 0x4B1F, 0x4C1B, 0x4D1F, 0x4E1F, 0x4F1B, 0x5007, 0x5103, 0x5203, 0x5307, 0x5403, 0x5507, 0x5607, 0x5703, 0x580B, 0x590F,
            0x5A1F, 0x5B1B, 0x5C1F, 0x5D1B, 0x5E1B, 0x5F1F, 0x6027, 0x6123, 0x6223, 0x6327, 0x6423, 0x6527, 0x6627, 0x6723, 0x682B, 0x692F,
            0x6A3F, 0x6B3B, 0x6C3F, 0x6D3B, 0x6E3B, 0x6F3F, 0x7023, 0x7127, 0x7227, 0x7323, 0x7427, 0x7523, 0x7623, 0x7727, 0x782F, 0x792B,
            0x7A3B, 0x7B3F, 0x7C3B, 0x7D3F, 0x7E3F, 0x7F3B, 0x8083, 0x8187, 0x8287, 0x8383, 0x8487, 0x8583, 0x8683, 0x8787, 0x888F, 0x898B,
            0x8A9B, 0x8B9F, 0x8C9B, 0x8D9F, 0x8E9F, 0x8F9B, 0x9087, 0x9183, 0x9283, 0x9387, 0x9483, 0x9587, 0x9687, 0x9783, 0x988B, 0x998F,
        };

        private byte IXH
        {
            get => (byte)(Regs.IX >> 8);
            set => Regs.IX = (ushort)((value << 8) | (Regs.IX & 0x00FF));
        }

        private byte IXL
        {
            get => (byte)(Regs.IX & 0x00FF);
            set => Regs.IX = (ushort)((Regs.IX & 0xFF00) | value);
        }

        private byte IYH
        {
            get => (byte)(Regs.IY >> 8);
            set => Regs.IY = (ushort)((value << 8) | (Regs.IY & 0x00FF));
        }

        private byte IYL
        {
            get => (byte)(Regs.IY & 0x00FF);
            set => Regs.IY = (ushort)((Regs.IY & 0xFF00) | value);
        }

        public Z80Cpu()
        {
            InitializeOpcodeTable();
            InitializeCBTable();
            InitializeEDTable();
            InitializeDDTable();
            InitializeFDTable();
        }

        // =========================================================
        // Public control
        // =========================================================

        public void Reset()
        {
            Regs.PC = 0;
            Regs.SP = 0xFFFF;
            Regs.I = 0;
            Regs.R = 0;

            halted = false;
            InterruptPending = false;
            IFF1 = false;
            IFF2 = false;

            eiDelay = 0;
            interruptMode = 1;

            reportedHighRamEntry = false;
            recentTrace.Clear();
            TStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
        }

        public void ExecuteCycles(ulong cycles)
        {
            ulong target = TStates + cycles;

            while (TStates < target)
            {
                if (BeforeInstruction != null && BeforeInstruction(this))
                    continue;

                if (InterruptPending && IFF1)
                {
                    if (Regs.SP < 0x4000)
                    {
                        Trace?.Invoke($"INT with BAD SP: PC={Regs.PC:X4} SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
                    }

                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;
                    IFF2 = false;

                    Push(Regs.PC);

                    switch (interruptMode)
                    {
                        case 0:
                        case 1:
                            Regs.PC = 0x0038;
                            break;

                        case 2:
                            ushort vector = (ushort)((Regs.I << 8) | 0xFF);
                            byte low = ReadMemory(vector);
                            byte high = ReadMemory((ushort)(vector + 1));
                            Regs.PC = (ushort)(low | (high << 8));
                            break;
                    }

                    TStates += 13;
                    continue;
                }

                if (halted)
                {
                    TStates += 4;
                    continue;
                }

                Step();
            }
        }

        public void Step()
        {
            ushort pcBefore = Regs.PC;
            ushort spBefore = Regs.SP;
            ushort ixBefore = Regs.IX;
            ushort iyBefore = Regs.IY;
            byte fBefore = Regs.F;

            byte op = FetchByte();

            RecordTrace(pcBefore, op);

            if (!reportedHighRamEntry && pcBefore >= 0xC000)
            {
                reportedHighRamEntry = true;
                Trace?.Invoke("=== ENTERED HIGH RAM ===");
                foreach (var line in recentTrace)
                    Trace?.Invoke(line);
            }

            if (op == 0xCB)
            {
                byte cbOp = FetchByte();
                cbOpcodeTable[cbOp]();
            }
            else if (op == 0xED)
            {
                byte edOp = FetchByte();
                edOpcodeTable[edOp]();
            }
            else if (op == 0xDD)
            {
                byte ddOp = FetchByte();
                if (ddOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IX, disp, cbOp);
                }
                else
                {
                    ddOpcodeTable[ddOp]();
                }
            }
            else if (op == 0xFD)
            {
                byte fdOp = FetchByte();
                if (fdOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IY, disp, cbOp);
                }
                else
                {
                    fdOpcodeTable[fdOp]();
                }
            }
            else
            {
                opcodeTable[op]();
            }

            if (spBefore != Regs.SP || ixBefore != Regs.IX || iyBefore != Regs.IY)
            {
                Trace?.Invoke(
                    $"STATE PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} IX {ixBefore:X4}->{Regs.IX:X4} IY {iyBefore:X4}->{Regs.IY:X4}");
            }

            if (Regs.SP < 0x4000)
            {
                Trace?.Invoke(
                    $"BAD SP after PC={pcBefore:X4} OP={op:X2}: SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
            }

            if (eiDelay > 0)
            {
                eiDelay--;
                if (eiDelay == 0)
                {
                    IFF1 = true;
                    IFF2 = true;
                }
            }

            lastFlagsBeforeInstruction = fBefore;
            flagsChangedLastInstruction = Regs.F != fBefore;
            qFlags = (Regs.F != fBefore) ? Regs.F : (byte)0;
        }

        public void RestoreInterruptState(bool iff1, bool iff2, int interruptMode)
        {
            IFF1 = iff1;
            IFF2 = iff2;
            this.interruptMode = interruptMode & 0x03;
            eiDelay = 0;
        }

        public void ClearSnapshotExecutionState()
        {
            halted = false;
            InterruptPending = false;
            TStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;

        }

        public void AdvanceTStates(uint tStates)
        {
            TStates += tStates;
        }

        // =========================================================
        // Opcode tables
        // =========================================================

        private void InitializeOpcodeTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                opcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL OP 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 1):X4} SP=0x{Regs.SP:X4}");
                    TStates += 4;
                };
            }

            opcodeTable[0x00] = () => TStates += 4; // NOP

            opcodeTable[0x07] = () => // RLCA
            {
                bool carry = (Regs.A & 0x80) != 0;
                Regs.A = (byte)((Regs.A << 1) | (carry ? 1 : 0));
                SetFlag(Flag.C, carry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);                
                TStates += 4;
            };

            opcodeTable[0x0F] = () => // RRCA
            {
                bool carry = (Regs.A & 0x01) != 0;
                Regs.A = (byte)((Regs.A >> 1) | (carry ? 0x80 : 0x00));
                SetFlag(Flag.C, carry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x17] = () => // RLA
            {
                bool oldCarry = (Regs.F & 0x01) != 0;
                bool newCarry = (Regs.A & 0x80) != 0;
                Regs.A = (byte)((Regs.A << 1) | (oldCarry ? 1 : 0));
                SetFlag(Flag.C, newCarry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x1F] = () => // RRA
            {
                bool oldCarry = (Regs.F & 0x01) != 0;
                bool newCarry = (Regs.A & 0x01) != 0;
                Regs.A = (byte)((Regs.A >> 1) | (oldCarry ? 0x80 : 0x00));
                SetFlag(Flag.C, newCarry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x08] = () => // EX AF,AF'
            {
                ushort temp = Regs.AF;
                Regs.AF = (ushort)((Regs.A_ << 8) | Regs.F_);
                Regs.A_ = (byte)(temp >> 8);
                Regs.F_ = (byte)temp;
                TStates += 4;
            };

            opcodeTable[0x02] = () => { WriteMemory(Regs.BC, Regs.A); TStates += 7; };
            opcodeTable[0x0A] = () => { Regs.A = ReadMemory(Regs.BC); TStates += 7; };
            opcodeTable[0x12] = () => { WriteMemory(Regs.DE, Regs.A); TStates += 7; };
            opcodeTable[0x1A] = () => { Regs.A = ReadMemory(Regs.DE); TStates += 7; };

            opcodeTable[0x09] = () => { Regs.HL = Add16(Regs.HL, Regs.BC); TStates += 11; };
            opcodeTable[0x19] = () => { Regs.HL = Add16(Regs.HL, Regs.DE); TStates += 11; };
            opcodeTable[0x29] = () => { Regs.HL = Add16(Regs.HL, Regs.HL); TStates += 11; };
            opcodeTable[0x39] = () => { Regs.HL = Add16(Regs.HL, Regs.SP); TStates += 11; };

            opcodeTable[0x10] = () => // DJNZ e
            {
                sbyte e = (sbyte)FetchByte();
                Regs.B--;
                if (Regs.B != 0)
                {
                    Regs.PC = (ushort)(Regs.PC + e);
                    TStates += 13;
                }
                else
                {
                    TStates += 8;
                }
            };

            opcodeTable[0x18] = () => // JR e
            {
                sbyte e = (sbyte)FetchByte();
                Regs.PC = (ushort)(Regs.PC + e);
                TStates += 12;
            };

            opcodeTable[0x20] = () => JRcc((Regs.F & 0x40) == 0); // JR NZ
            opcodeTable[0x28] = () => JRcc((Regs.F & 0x40) != 0); // JR Z
            opcodeTable[0x30] = () => JRcc((Regs.F & 0x01) == 0); // JR NC
            opcodeTable[0x38] = () => JRcc((Regs.F & 0x01) != 0); // JR C

            opcodeTable[0x22] = () => // LD (nn),HL
            {
                ushort addr = FetchWord();
                WriteMemory(addr, Regs.L);
                WriteMemory((ushort)(addr + 1), Regs.H);
                TStates += 16;
            };

            opcodeTable[0x27] = () => // DAA
            {
                const byte FlagC = 0x01;
                const byte FlagN = 0x02;
                const byte FlagH = 0x10;

                int index =
                    Regs.A
                    | ((Regs.F & FlagC) != 0 ? 0x100 : 0)
                    | ((Regs.F & FlagN) != 0 ? 0x200 : 0)
                    | ((Regs.F & FlagH) != 0 ? 0x400 : 0);

                ushort af = DaaAfTable[index];
                Regs.A = (byte)(af >> 8);
                Regs.F = (byte)af;

                TStates += 4;
            };

            opcodeTable[0x2A] = () => // LD HL,(nn)
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.HL = (ushort)(low | (high << 8));
                TStates += 16;
            };

            opcodeTable[0x2F] = () => // CPL
            {
                Regs.A = (byte)~Regs.A;
                SetFlag(Flag.N, true);
                SetFlag(Flag.H, true);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x37] = () => // SCF
            {
                SetFlag(Flag.C, true);
                SetFlag(Flag.N, false);
                SetFlag(Flag.H, false);
                ApplyScfCcfUndocumentedFlags();
                TStates += 4;
            };

            opcodeTable[0x3F] = () => // CCF
            {
                bool oldC = (Regs.F & (1 << (int)Flag.C)) != 0;
                SetFlag(Flag.C, !oldC);
                SetFlag(Flag.N, false);
                SetFlag(Flag.H, oldC);
                ApplyScfCcfUndocumentedFlags();
                TStates += 4;
            };

            for (int dst = 0; dst < 8; dst++)
            for (int src = 0; src < 8; src++)
            {
                if (dst == 6 && src == 6) continue;

                int op = 0x40 + (dst << 3) + src;
                int d = dst;
                int s = src;

                opcodeTable[op] = () =>
                {
                    SetReg((byte)d, GetReg((byte)s));
                    TStates += 4;
                };
            }

            opcodeTable[0x76] = () => // HALT
            {
                halted = true;
                TStates += 4;
            };

            for (int r = 0; r < 8; r++)
            {
                int op = 0x06 + (r << 3);
                int rr = r;
                opcodeTable[op] = () =>
                {
                    SetReg((byte)rr, FetchByte());
                    TStates += 7;
                };
            }

            opcodeTable[0x36] = () => // LD (HL),n
            {
                WriteMemory(Regs.HL, FetchByte());
                TStates += 10;
            };

            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                opcodeTable[0x70 + r] = () =>
                {
                    WriteMemory(Regs.HL, GetReg((byte)rr));
                    TStates += 7;
                };
            }

            opcodeTable[0x01] = () => { Regs.BC = FetchWord(); TStates += 10; };
            opcodeTable[0x11] = () => { Regs.DE = FetchWord(); TStates += 10; };
            opcodeTable[0x21] = () => { Regs.HL = FetchWord(); TStates += 10; };
            opcodeTable[0x31] = () => { Regs.SP = FetchWord(); TStates += 10; };

            opcodeTable[0x3A] = () => { Regs.A = ReadMemory(FetchWord()); TStates += 13; };
            opcodeTable[0x32] = () => { WriteMemory(FetchWord(), Regs.A); TStates += 13; };

            opcodeTable[0x03] = () => { Regs.BC++; TStates += 6; };
            opcodeTable[0x0B] = () => { Regs.BC--; TStates += 6; };
            opcodeTable[0x13] = () => { Regs.DE++; TStates += 6; };
            opcodeTable[0x1B] = () => { Regs.DE--; TStates += 6; };
            opcodeTable[0x23] = () => { Regs.HL++; TStates += 6; };
            opcodeTable[0x2B] = () => { Regs.HL--; TStates += 6; };
            opcodeTable[0x33] = () => { Regs.SP++; TStates += 6; };
            opcodeTable[0x3B] = () => { Regs.SP--; TStates += 6; };

            for (int r = 0; r < 8; r++)
            {
                int rr = r;
                opcodeTable[0x04 + (r << 3)] = () => IncReg((byte)rr);
                opcodeTable[0x05 + (r << 3)] = () => DecReg((byte)rr);
            }

            opcodeTable[0xD9] = () => // EXX
            {
                (Regs.B, Regs.B_) = (Regs.B_, Regs.B);
                (Regs.C, Regs.C_) = (Regs.C_, Regs.C);
                (Regs.D, Regs.D_) = (Regs.D_, Regs.D);
                (Regs.E, Regs.E_) = (Regs.E_, Regs.E);
                (Regs.H, Regs.H_) = (Regs.H_, Regs.H);
                (Regs.L, Regs.L_) = (Regs.L_, Regs.L);
                TStates += 4;
            };

            opcodeTable[0xEB] = () => // EX DE,HL
            {
                ushort temp = Regs.DE;
                Regs.DE = Regs.HL;
                Regs.HL = temp;
                TStates += 4;
            };

            opcodeTable[0xE3] = () => // EX (SP),HL
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, Regs.L);
                WriteMemory((ushort)(Regs.SP + 1), Regs.H);
                Regs.HL = (ushort)(low | (high << 8));
                TStates += 19;
            };

            opcodeTable[0xE9] = () => // JP (HL)
            {
                Regs.PC = Regs.HL;
                TStates += 4;
            };

            opcodeTable[0xF9] = () => // LD SP,HL
            {
                Regs.SP = Regs.HL;
                TStates += 6;
            };

            for (int i = 0; i < 8; i++)
            {
                int r = i;
                opcodeTable[0x80 + i] = () => AddA(GetReg((byte)r), false, 4);
                opcodeTable[0x88 + i] = () => AddA(GetReg((byte)r), true, 4);
                opcodeTable[0x90 + i] = () => SubA(GetReg((byte)r), false, 4);
                opcodeTable[0x98 + i] = () => SubA(GetReg((byte)r), true, 4);
                opcodeTable[0xA0 + i] = () => AndA(GetReg((byte)r), 4);
                opcodeTable[0xA8 + i] = () => XorA(GetReg((byte)r), 4);
                opcodeTable[0xB0 + i] = () => OrA(GetReg((byte)r), 4);
                opcodeTable[0xB8 + i] = () => CpA(GetReg((byte)r), 4);
            }

            opcodeTable[0xC6] = () => AddA(FetchByte(), false, 7);
            opcodeTable[0xCE] = () => AddA(FetchByte(), true, 7);
            opcodeTable[0xD6] = () => SubA(FetchByte(), false, 7);
            opcodeTable[0xDE] = () => SubA(FetchByte(), true, 7);
            opcodeTable[0xE6] = () => AndA(FetchByte(), 7);
            opcodeTable[0xEE] = () => XorA(FetchByte(), 7);
            opcodeTable[0xF6] = () => OrA(FetchByte(), 7);
            opcodeTable[0xFE] = () => CpA(FetchByte(), 7);

            opcodeTable[0xC3] = () => // JP nn
            {
                Regs.PC = FetchWord();
                TStates += 10;
            };

            opcodeTable[0xC2] = () => JPcc((Regs.F & 0x40) == 0);
            opcodeTable[0xCA] = () => JPcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD2] = () => JPcc((Regs.F & 0x01) == 0);
            opcodeTable[0xDA] = () => JPcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE2] = () => JPcc((Regs.F & 0x04) == 0);
            opcodeTable[0xEA] = () => JPcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF2] = () => JPcc((Regs.F & 0x80) == 0);
            opcodeTable[0xFA] = () => JPcc((Regs.F & 0x80) != 0);

            opcodeTable[0xCD] = () => // CALL nn
            {
                ushort addr = FetchWord();
                Push(Regs.PC);
                Regs.PC = addr;
                TStates += 17;
            };

            opcodeTable[0xC4] = () => CALLcc((Regs.F & 0x40) == 0);
            opcodeTable[0xCC] = () => CALLcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD4] = () => CALLcc((Regs.F & 0x01) == 0);
            opcodeTable[0xDC] = () => CALLcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE4] = () => CALLcc((Regs.F & 0x04) == 0);
            opcodeTable[0xEC] = () => CALLcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF4] = () => CALLcc((Regs.F & 0x80) == 0);
            opcodeTable[0xFC] = () => CALLcc((Regs.F & 0x80) != 0);

            opcodeTable[0xC9] = () => // RET
            {
                Regs.PC = Pop();
                TStates += 10;
            };

            opcodeTable[0xC0] = () => RETcc((Regs.F & 0x40) == 0);
            opcodeTable[0xC8] = () => RETcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD0] = () => RETcc((Regs.F & 0x01) == 0);
            opcodeTable[0xD8] = () => RETcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE0] = () => RETcc((Regs.F & 0x04) == 0);
            opcodeTable[0xE8] = () => RETcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF0] = () => RETcc((Regs.F & 0x80) == 0);
            opcodeTable[0xF8] = () => RETcc((Regs.F & 0x80) != 0);

            opcodeTable[0xC5] = () => { Push(Regs.BC); TStates += 11; };
            opcodeTable[0xD5] = () => { Push(Regs.DE); TStates += 11; };
            opcodeTable[0xE5] = () => { Push(Regs.HL); TStates += 11; };
            opcodeTable[0xF5] = () => { Push(Regs.AF); TStates += 11; };

            opcodeTable[0xC1] = () => { Regs.BC = Pop(); TStates += 10; };
            opcodeTable[0xD1] = () => { Regs.DE = Pop(); TStates += 10; };
            opcodeTable[0xE1] = () => { Regs.HL = Pop(); TStates += 10; };
            opcodeTable[0xF1] = () => { Regs.AF = Pop(); TStates += 10; };

            opcodeTable[0xF3] = () => // DI
            {
                IFF1 = false;
                IFF2 = false;
                eiDelay = 0;
                TStates += 4;
            };

            opcodeTable[0xFB] = () => // EI
            {
                eiDelay = 2;
                TStates += 4;
            };

            opcodeTable[0xD3] = () => // OUT (n),A
            {
                byte low = FetchByte();
                ushort port = (ushort)((Regs.A << 8) | low);
                WritePort(port, Regs.A);
                TStates += 11;
            };

            opcodeTable[0xDB] = () => // IN A,(n)
            {
                byte low = FetchByte();
                ushort port = (ushort)((Regs.A << 8) | low);
                Regs.A = ReadPort(port);
                TStates += 11;
            };

            for (int i = 0; i < 8; i++)
            {
                int addr = i * 8;
                opcodeTable[0xC7 + i * 8] = () =>
                {
                    Push(Regs.PC);
                    Regs.PC = (ushort)addr;
                    TStates += 11;
                };
            }
        }

        private void InitializeCBTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                cbOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL CB 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4}");
                    TStates += 8;
                };
            }

            for (int r = 0; r < 8; r++)
            {
                int rr = r;

                cbOpcodeTable[0x00 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | (c ? 1 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x08 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (c ? 0x80 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x10 + r] = () => { byte v = GetReg((byte)rr); bool oldC = (Regs.F & 0x01) != 0; bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | (oldC ? 1 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x18 + r] = () => { byte v = GetReg((byte)rr); bool oldC = (Regs.F & 0x01) != 0; bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (oldC ? 0x80 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x20 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)(v << 1); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x28 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (v & 0x80)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x30 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | 0x01); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x38 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)(v >> 1); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0x40 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    bool set = (val & (1 << b)) != 0;
                    SetFlag(Flag.Z, !set);
                    SetFlag(Flag.N, false);
                    SetFlag(Flag.H, true);
                    SetFlag(Flag.S, b == 7 && set);
                    SetFlag(Flag.P, !set);
                    TStates += (r == 6 ? 12UL : 8UL);
                };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0x80 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val & ~(1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0xC0 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val | (1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }
        }

        private void InitializeEDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                edOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL ED 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4}");
                    TStates += 8;
                };
            }

            // =========================
            // IN r,(C)
            // =========================
            edOpcodeTable[0x40] = () => { byte v = ReadPort(Regs.BC); Regs.B = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x48] = () => { byte v = ReadPort(Regs.BC); Regs.C = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x50] = () => { byte v = ReadPort(Regs.BC); Regs.D = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x58] = () => { byte v = ReadPort(Regs.BC); Regs.E = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x60] = () => { byte v = ReadPort(Regs.BC); Regs.H = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x68] = () => { byte v = ReadPort(Regs.BC); Regs.L = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x70] = () => { byte v = ReadPort(Regs.BC); SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x78] = () => { byte v = ReadPort(Regs.BC); Regs.A = v; SetInFlags(v); TStates += 12; };

            // =========================
            // OUT (C),r
            // =========================
            edOpcodeTable[0x41] = () => { WritePort(Regs.BC, Regs.B); TStates += 12; };
            edOpcodeTable[0x49] = () => { WritePort(Regs.BC, Regs.C); TStates += 12; };
            edOpcodeTable[0x51] = () => { WritePort(Regs.BC, Regs.D); TStates += 12; };
            edOpcodeTable[0x59] = () => { WritePort(Regs.BC, Regs.E); TStates += 12; };
            edOpcodeTable[0x61] = () => { WritePort(Regs.BC, Regs.H); TStates += 12; };
            edOpcodeTable[0x69] = () => { WritePort(Regs.BC, Regs.L); TStates += 12; };
            edOpcodeTable[0x71] = () => { WritePort(Regs.BC, 0); TStates += 12; };
            edOpcodeTable[0x79] = () => { WritePort(Regs.BC, Regs.A); TStates += 12; };

            // =========================
            // Transfer A <-> I/R
            // =========================
            edOpcodeTable[0x47] = () => { Regs.I = Regs.A; TStates += 9; };
            edOpcodeTable[0x4F] = () => { Regs.R = Regs.A; TStates += 9; };

            edOpcodeTable[0x57] = () => // LD A,I
            {
                Regs.A = Regs.I;
                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, IFF2);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 9;
            };

            edOpcodeTable[0x5F] = () => // LD A,R
            {
                Regs.A = Regs.R;
                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, IFF2);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 9;
            };

            // =========================
            // SBC/ADC HL,rr
            // =========================
            edOpcodeTable[0x42] = () => { Regs.HL = Sub16(Regs.HL, Regs.BC, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x52] = () => { Regs.HL = Sub16(Regs.HL, Regs.DE, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x62] = () => { Regs.HL = Sub16(Regs.HL, Regs.HL, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x72] = () => { Regs.HL = Sub16(Regs.HL, Regs.SP, (Regs.F & 0x01) != 0); TStates += 15; };

            edOpcodeTable[0x4A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.BC, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x5A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.DE, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x6A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.HL, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x7A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.SP, (Regs.F & 0x01) != 0); TStates += 15; };

            // =========================
            // 16-bit loads via memory
            // =========================
            edOpcodeTable[0x43] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.C); WriteMemory((ushort)(a + 1), Regs.B); TStates += 20; };
            edOpcodeTable[0x53] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.E); WriteMemory((ushort)(a + 1), Regs.D); TStates += 20; };
            edOpcodeTable[0x63] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.L); WriteMemory((ushort)(a + 1), Regs.H); TStates += 20; };
            edOpcodeTable[0x73] = () => { ushort a = FetchWord(); WriteMemory(a, (byte)(Regs.SP & 0xFF)); WriteMemory((ushort)(a + 1), (byte)(Regs.SP >> 8)); TStates += 20; };

            edOpcodeTable[0x4B] = () => { ushort a = FetchWord(); Regs.BC = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x5B] = () => { ushort a = FetchWord(); Regs.DE = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x6B] = () => { ushort a = FetchWord(); Regs.HL = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x7B] = () => { ushort a = FetchWord(); Regs.SP = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };

            // =========================
            // NEG (ED prefix)
            // =========================
            // All of these opcodes are undocumented aliases of NEG.
            // Z80 defines 8 encodings (ED 44,4C,54,5C,64,6C,74,7C)
            // that all perform: A = 0 - A with full flag behaviour.
            // Must map ALL of them for ZEXDOC/ZEXALL compliance.
            edOpcodeTable[0x44] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x4C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x54] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x5C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x64] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x6C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x74] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x7C] = () => { NegA(); TStates += 8; };

            // =========================
            // Return / interrupt mode
            // =========================
            edOpcodeTable[0x45] = () => { IFF1 = IFF2; Regs.PC = Pop(); TStates += 14; }; // RETN
            edOpcodeTable[0x4D] = () => { IFF1 = IFF2; Regs.PC = Pop(); TStates += 14; }; // RETI

            edOpcodeTable[0x46] = () => { interruptMode = 0; TStates += 8; };
            edOpcodeTable[0x56] = () => { interruptMode = 1; TStates += 8; };
            edOpcodeTable[0x5E] = () => { interruptMode = 2; TStates += 8; };

            // =========================
            // Decimal rotate through memory
            // =========================
            edOpcodeTable[0x67] = () => // RRD
            {
                byte mem = ReadMemory(Regs.HL);
                byte aLow = (byte)(Regs.A & 0x0F);
                byte newMem = (byte)((aLow << 4) | (mem >> 4));
                byte newA = (byte)((Regs.A & 0xF0) | (mem & 0x0F));

                WriteMemory(Regs.HL, newMem);
                Regs.A = newA;

                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, Parity(Regs.A));
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);

                TStates += 18;
            };

            edOpcodeTable[0x6F] = () => // RLD
            {
                byte mem = ReadMemory(Regs.HL);
                byte aLow = (byte)(Regs.A & 0x0F);
                byte newMem = (byte)((mem << 4) | aLow);
                byte newA = (byte)((Regs.A & 0xF0) | (mem >> 4));

                WriteMemory(Regs.HL, newMem);
                Regs.A = newA;

                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, Parity(Regs.A));
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);

                TStates += 18;
            };

            // =========================
            // Block transfer
            // =========================
            edOpcodeTable[0xA0] = () => // LDI
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL++;
                Regs.DE++;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                TStates += 16;
            };

            edOpcodeTable[0xB0] = () => // LDIR
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL++;
                Regs.DE++;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                if (Regs.BC != 0)
                {
                    Regs.PC = (ushort)(Regs.PC - 2);
                    TStates += 21;
                }
                else
                {
                    TStates += 16;
                }
            };

            edOpcodeTable[0xA8] = () => // LDD
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL--;
                Regs.DE--;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                TStates += 16;
            };

            edOpcodeTable[0xB8] = () => // LDDR
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL--;
                Regs.DE--;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                if (Regs.BC != 0)
                {
                    Regs.PC = (ushort)(Regs.PC - 2);
                    TStates += 21;
                }
                else
                {
                    TStates += 16;
                }
            };

            // =========================
            // Block compare
            // =========================
            edOpcodeTable[0xA1] = () => BlockCompare(true, false);  // CPI
            edOpcodeTable[0xB1] = () => BlockCompare(true, true);   // CPIR
            edOpcodeTable[0xA9] = () => BlockCompare(false, false); // CPD
            edOpcodeTable[0xB9] = () => BlockCompare(false, true);  // CPDR

            // =========================
            // Block I/O
            // =========================
            edOpcodeTable[0xA2] = () => BlockIn(true, false);   // INI
            edOpcodeTable[0xB2] = () => BlockIn(true, true);    // INIR
            edOpcodeTable[0xAA] = () => BlockIn(false, false);  // IND
            edOpcodeTable[0xBA] = () => BlockIn(false, true);   // INDR

            edOpcodeTable[0xA3] = () => BlockOut(true, false);  // OUTI
            edOpcodeTable[0xB3] = () => BlockOut(true, true);   // OTIR
            edOpcodeTable[0xAB] = () => BlockOut(false, false); // OUTD
            edOpcodeTable[0xBB] = () => BlockOut(false, true);  // OTDR
        }

        private void BlockIn(bool increment, bool repeat)
        {
            byte value = ReadPort(Regs.BC);
            WriteMemory(Regs.HL, value);

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.B = (byte)(Regs.B - 1);

            // Minimal first-pass flag behaviour:
            // N is set
            // Z reflects B == 0
            // Other flags can be refined later if needed for compatibility
            SetFlag(Flag.N, true);
            SetFlag(Flag.Z, Regs.B == 0);

            if (repeat && Regs.B != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                TStates += 21;
            }
            else
            {
                TStates += 16;
            }
        }

        private void BlockOut(bool increment, bool repeat)
        {
            byte value = ReadMemory(Regs.HL);
            WritePort(Regs.BC, value);

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.B = (byte)(Regs.B - 1);

            SetFlag(Flag.N, true);
            SetFlag(Flag.Z, Regs.B == 0);

            if (repeat && Regs.B != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                TStates += 21;
            }
            else
            {
                TStates += 16;
            }
        }

        private void CopyUndocumentedFlagsFrom(byte value)
        {
            SetFlag(Flag.F3, (value & 0x08) != 0);
            SetFlag(Flag.F5, (value & 0x20) != 0);
        }
        
        private void ApplyScfCcfUndocumentedFlags()
        {
            byte f3f5 = (byte)(((qFlags ^ Regs.F) | Regs.A) & 0x28);
            Regs.F = (byte)((Regs.F & 0xD7) | f3f5);
        }

        private byte CompareAInternal(byte value)
        {
            byte a = Regs.A;
            int result = a - value;
            byte r = (byte)result;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ value ^ r) & 0x10) != 0);
            SetFlag(Flag.P, OverflowSub(a, value, r));
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);

            CopyUndocumentedFlagsFrom(r);
            return r;
        }        

        private void BlockCompare(bool increment, bool repeat)
        {
            bool oldCarry = (Regs.F & (1 << (int)Flag.C)) != 0;

            byte value = ReadMemory(Regs.HL);
            byte r = CompareAInternal(value);

            bool halfBorrow = (Regs.F & (1 << (int)Flag.H)) != 0;

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.BC--;

            SetFlag(Flag.P, Regs.BC != 0);

            byte n = (byte)(r - (halfBorrow ? 1 : 0));
            SetFlag(Flag.F3, (n & 0x08) != 0);
            SetFlag(Flag.F5, (n & 0x20) != 0);

            SetFlag(Flag.C, oldCarry);

            if (repeat && Regs.BC != 0 && r != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                TStates += 21;
            }
            else
            {
                TStates += 16;
            }
        }

        private void InitializeDDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                ddOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL DD 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4} IX=0x{Regs.IX:X4}");
                    opcodeTable[op]();
                };
            }

            // 16-bit IX core
            ddOpcodeTable[0x09] = () => { Regs.IX = Add16(Regs.IX, Regs.BC); TStates += 15; };
            ddOpcodeTable[0x19] = () => { Regs.IX = Add16(Regs.IX, Regs.DE); TStates += 15; };
            ddOpcodeTable[0x21] = () => { Regs.IX = FetchWord(); TStates += 10; };
            ddOpcodeTable[0x22] = () =>
            {
                ushort addr = FetchWord();
                WriteMemory(addr, (byte)(Regs.IX & 0xFF));
                WriteMemory((ushort)(addr + 1), (byte)(Regs.IX >> 8));
                TStates += 16;
            };
            ddOpcodeTable[0x23] = () => { Regs.IX++; TStates += 10; };
            ddOpcodeTable[0x24] = () => { IXH = Inc8(IXH); TStates += 8; };
            ddOpcodeTable[0x25] = () => { IXH = Dec8(IXH); TStates += 8; };
            ddOpcodeTable[0x26] = () => { IXH = FetchByte(); TStates += 11; };
            ddOpcodeTable[0x29] = () => { Regs.IX = Add16(Regs.IX, Regs.IX); TStates += 15; };
            ddOpcodeTable[0x2A] = () =>
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.IX = (ushort)(low | (high << 8));
                TStates += 16;
            };
            ddOpcodeTable[0x2B] = () => { Regs.IX--; TStates += 10; };
            ddOpcodeTable[0x2C] = () => { IXL = Inc8(IXL); TStates += 8; };
            ddOpcodeTable[0x2D] = () => { IXL = Dec8(IXL); TStates += 8; };
            ddOpcodeTable[0x2E] = () => { IXL = FetchByte(); TStates += 11; };
            ddOpcodeTable[0x34] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IX + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old + 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x0F);
                SetFlag(Flag.P, old == 0x7F);
                SetFlag(Flag.N, false);
                TStates += 23;
            };
            ddOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IX + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old - 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x00);
                SetFlag(Flag.P, old == 0x80);
                SetFlag(Flag.N, true);
                TStates += 23;
            };
            ddOpcodeTable[0x36] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                byte n = FetchByte();
                WriteMemory((ushort)(Regs.IX + d), n);
                TStates += 19;
            };
            ddOpcodeTable[0x39] = () => { Regs.IX = Add16(Regs.IX, Regs.SP); TStates += 15; };

            // LD r, IXH/IXL/(IX+d)
            ddOpcodeTable[0x44] = () => { Regs.B = IXH; TStates += 8; };
            ddOpcodeTable[0x45] = () => { Regs.B = IXL; TStates += 8; };
            ddOpcodeTable[0x46] = () => { sbyte d = (sbyte)FetchByte(); Regs.B = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x4C] = () => { Regs.C = IXH; TStates += 8; };
            ddOpcodeTable[0x4D] = () => { Regs.C = IXL; TStates += 8; };
            ddOpcodeTable[0x4E] = () => { sbyte d = (sbyte)FetchByte(); Regs.C = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x54] = () => { Regs.D = IXH; TStates += 8; };
            ddOpcodeTable[0x55] = () => { Regs.D = IXL; TStates += 8; };
            ddOpcodeTable[0x56] = () => { sbyte d = (sbyte)FetchByte(); Regs.D = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x5C] = () => { Regs.E = IXH; TStates += 8; };
            ddOpcodeTable[0x5D] = () => { Regs.E = IXL; TStates += 8; };
            ddOpcodeTable[0x5E] = () => { sbyte d = (sbyte)FetchByte(); Regs.E = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            // LD IXH/IXL, r
            ddOpcodeTable[0x60] = () => { IXH = Regs.B; TStates += 8; };
            ddOpcodeTable[0x61] = () => { IXH = Regs.C; TStates += 8; };
            ddOpcodeTable[0x62] = () => { IXH = Regs.D; TStates += 8; };
            ddOpcodeTable[0x63] = () => { IXH = Regs.E; TStates += 8; };

            ddOpcodeTable[0x64] = () => { IXH = IXH; TStates += 8; };
            ddOpcodeTable[0x65] = () => { IXH = IXL; TStates += 8; };
            ddOpcodeTable[0x6C] = () => { IXL = IXH; TStates += 8; };
            ddOpcodeTable[0x6D] = () => { IXL = IXL; TStates += 8; };

            ddOpcodeTable[0x67] = () => { IXH = Regs.A; TStates += 8; };
            ddOpcodeTable[0x68] = () => { IXL = Regs.B; TStates += 8; };
            ddOpcodeTable[0x69] = () => { IXL = Regs.C; TStates += 8; };
            ddOpcodeTable[0x6A] = () => { IXL = Regs.D; TStates += 8; };
            ddOpcodeTable[0x6B] = () => { IXL = Regs.E; TStates += 8; };

            ddOpcodeTable[0x66] = () => { sbyte d = (sbyte)FetchByte(); Regs.H = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };
            ddOpcodeTable[0x6E] = () => { sbyte d = (sbyte)FetchByte(); Regs.L = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };
            ddOpcodeTable[0x6F] = () => { IXL = Regs.A; TStates += 8; };

            // LD (IX+d), r
            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                ddOpcodeTable[0x70 + r] = () =>
                {
                    sbyte d = (sbyte)FetchByte();
                    ushort addr = (ushort)(Regs.IX + d);
                    byte value = rr switch
                    {
                        0 => Regs.B,
                        1 => Regs.C,
                        2 => Regs.D,
                        3 => Regs.E,
                        4 => Regs.H,
                        5 => Regs.L,
                        7 => Regs.A,
                        _ => 0
                    };
                    WriteMemory(addr, value);
                    TStates += 19;
                };
            }

            ddOpcodeTable[0x7C] = () => { Regs.A = IXH; TStates += 8; };
            ddOpcodeTable[0x7D] = () => { Regs.A = IXL; TStates += 8; };
            ddOpcodeTable[0x7E] = () => { sbyte d = (sbyte)FetchByte(); Regs.A = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            // ALU IXH/IXL/(IX+d)
            ddOpcodeTable[0x84] = () => AddA(IXH, false, 8);
            ddOpcodeTable[0x85] = () => AddA(IXL, false, 8);
            ddOpcodeTable[0x86] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IX + d)), false, 19); };
            ddOpcodeTable[0x8C] = () => AddA(IXH, true, 8);
            ddOpcodeTable[0x8D] = () => AddA(IXL, true, 8);
            ddOpcodeTable[0x8E] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IX + d)), true, 19); };

            ddOpcodeTable[0x94] = () => SubA(IXH, false, 8);
            ddOpcodeTable[0x95] = () => SubA(IXL, false, 8);
            ddOpcodeTable[0x96] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IX + d)), false, 19); };
            ddOpcodeTable[0x9C] = () => SubA(IXH, true, 8);
            ddOpcodeTable[0x9D] = () => SubA(IXL, true, 8);
            ddOpcodeTable[0x9E] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IX + d)), true, 19); };

            ddOpcodeTable[0xA4] = () => AndA(IXH, 8);
            ddOpcodeTable[0xA5] = () => AndA(IXL, 8);
            ddOpcodeTable[0xA6] = () => { sbyte d = (sbyte)FetchByte(); AndA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xAC] = () => XorA(IXH, 8);
            ddOpcodeTable[0xAD] = () => XorA(IXL, 8);
            ddOpcodeTable[0xAE] = () => { sbyte d = (sbyte)FetchByte(); XorA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xB4] = () => OrA(IXH, 8);
            ddOpcodeTable[0xB5] = () => OrA(IXL, 8);
            ddOpcodeTable[0xB6] = () => { sbyte d = (sbyte)FetchByte(); OrA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xBC] = () => CpA(IXH, 8);
            ddOpcodeTable[0xBD] = () => CpA(IXL, 8);
            ddOpcodeTable[0xBE] = () => { sbyte d = (sbyte)FetchByte(); CpA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xE1] = () => { Regs.IX = Pop(); TStates += 10; };
            ddOpcodeTable[0xE3] = () =>
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, (byte)(Regs.IX & 0xFF));
                WriteMemory((ushort)(Regs.SP + 1), (byte)(Regs.IX >> 8));
                Regs.IX = (ushort)(low | (high << 8));
                TStates += 19;
            };
            ddOpcodeTable[0xE5] = () => { Push(Regs.IX); TStates += 11; };
            ddOpcodeTable[0xE9] = () => { Regs.PC = Regs.IX; TStates += 8; };
            ddOpcodeTable[0xF9] = () => { Regs.SP = Regs.IX; TStates += 6; };
        }

        private void InitializeFDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                fdOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL FD 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4} IY=0x{Regs.IY:X4}");
                    opcodeTable[op]();
                };
            }

            // 16-bit IY core
            fdOpcodeTable[0x09] = () => { Regs.IY = Add16(Regs.IY, Regs.BC); TStates += 15; };
            fdOpcodeTable[0x19] = () => { Regs.IY = Add16(Regs.IY, Regs.DE); TStates += 15; };
            fdOpcodeTable[0x21] = () => { Regs.IY = FetchWord(); TStates += 10; };
            fdOpcodeTable[0x22] = () =>
            {
                ushort addr = FetchWord();
                WriteMemory(addr, (byte)(Regs.IY & 0xFF));
                WriteMemory((ushort)(addr + 1), (byte)(Regs.IY >> 8));
                TStates += 16;
            };
            fdOpcodeTable[0x23] = () => { Regs.IY++; TStates += 10; };
            fdOpcodeTable[0x24] = () => { IYH = Inc8(IYH); TStates += 8; };
            fdOpcodeTable[0x25] = () => { IYH = Dec8(IYH); TStates += 8; };
            fdOpcodeTable[0x26] = () => { IYH = FetchByte(); TStates += 11; };
            fdOpcodeTable[0x29] = () => { Regs.IY = Add16(Regs.IY, Regs.IY); TStates += 15; };
            fdOpcodeTable[0x2A] = () =>
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.IY = (ushort)(low | (high << 8));
                TStates += 16;
            };
            fdOpcodeTable[0x2B] = () => { Regs.IY--; TStates += 10; };
            fdOpcodeTable[0x2C] = () => { IYL = Inc8(IYL); TStates += 8; };
            fdOpcodeTable[0x2D] = () => { IYL = Dec8(IYL); TStates += 8; };
            fdOpcodeTable[0x2E] = () => { IYL = FetchByte(); TStates += 11; };
            fdOpcodeTable[0x34] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IY + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old + 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x0F);
                SetFlag(Flag.P, old == 0x7F);
                SetFlag(Flag.N, false);
                TStates += 23;
            };
            fdOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IY + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old - 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x00);
                SetFlag(Flag.P, old == 0x80);
                SetFlag(Flag.N, true);
                TStates += 23;
            };
            fdOpcodeTable[0x36] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                byte n = FetchByte();
                WriteMemory((ushort)(Regs.IY + d), n);
                TStates += 19;
            };
            fdOpcodeTable[0x39] = () => { Regs.IY = Add16(Regs.IY, Regs.SP); TStates += 15; };

            // LD r, IYH/IYL/(IY+d)
            fdOpcodeTable[0x44] = () => { Regs.B = IYH; TStates += 8; };
            fdOpcodeTable[0x45] = () => { Regs.B = IYL; TStates += 8; };
            fdOpcodeTable[0x46] = () => { sbyte d = (sbyte)FetchByte(); Regs.B = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x4C] = () => { Regs.C = IYH; TStates += 8; };
            fdOpcodeTable[0x4D] = () => { Regs.C = IYL; TStates += 8; };
            fdOpcodeTable[0x4E] = () => { sbyte d = (sbyte)FetchByte(); Regs.C = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x54] = () => { Regs.D = IYH; TStates += 8; };
            fdOpcodeTable[0x55] = () => { Regs.D = IYL; TStates += 8; };
            fdOpcodeTable[0x56] = () => { sbyte d = (sbyte)FetchByte(); Regs.D = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x5C] = () => { Regs.E = IYH; TStates += 8; };
            fdOpcodeTable[0x5D] = () => { Regs.E = IYL; TStates += 8; };
            fdOpcodeTable[0x5E] = () => { sbyte d = (sbyte)FetchByte(); Regs.E = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            // LD IYH/IYL, r
            fdOpcodeTable[0x60] = () => { IYH = Regs.B; TStates += 8; };
            fdOpcodeTable[0x61] = () => { IYH = Regs.C; TStates += 8; };
            fdOpcodeTable[0x62] = () => { IYH = Regs.D; TStates += 8; };
            fdOpcodeTable[0x63] = () => { IYH = Regs.E; TStates += 8; };
            fdOpcodeTable[0x64] = () => { IYH = IYH; TStates += 8; };
            fdOpcodeTable[0x65] = () => { IYH = IYL; TStates += 8; };
            fdOpcodeTable[0x67] = () => { IYH = Regs.A; TStates += 8; };

            fdOpcodeTable[0x68] = () => { IYL = Regs.B; TStates += 8; };
            fdOpcodeTable[0x69] = () => { IYL = Regs.C; TStates += 8; };
            fdOpcodeTable[0x6A] = () => { IYL = Regs.D; TStates += 8; };
            fdOpcodeTable[0x6B] = () => { IYL = Regs.E; TStates += 8; };
            fdOpcodeTable[0x6C] = () => { IYL = IYH; TStates += 8; };
            fdOpcodeTable[0x6D] = () => { IYL = IYL; TStates += 8; };            

            fdOpcodeTable[0x66] = () => { sbyte d = (sbyte)FetchByte(); Regs.H = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };
            fdOpcodeTable[0x6E] = () => { sbyte d = (sbyte)FetchByte(); Regs.L = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };
            fdOpcodeTable[0x6F] = () => { IYL = Regs.A; TStates += 8; };

            // LD (IY+d), r
            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                fdOpcodeTable[0x70 + r] = () =>
                {
                    sbyte d = (sbyte)FetchByte();
                    ushort addr = (ushort)(Regs.IY + d);
                    byte value = rr switch
                    {
                        0 => Regs.B,
                        1 => Regs.C,
                        2 => Regs.D,
                        3 => Regs.E,
                        4 => Regs.H,
                        5 => Regs.L,
                        7 => Regs.A,
                        _ => 0
                    };
                    WriteMemory(addr, value);
                    TStates += 19;
                };
            }

            fdOpcodeTable[0x7C] = () => { Regs.A = IYH; TStates += 8; };
            fdOpcodeTable[0x7D] = () => { Regs.A = IYL; TStates += 8; };
            fdOpcodeTable[0x7E] = () => { sbyte d = (sbyte)FetchByte(); Regs.A = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            // ALU IYH/IYL/(IY+d)
            fdOpcodeTable[0x84] = () => AddA(IYH, false, 8);
            fdOpcodeTable[0x85] = () => AddA(IYL, false, 8);
            fdOpcodeTable[0x86] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IY + d)), false, 19); };
            fdOpcodeTable[0x8C] = () => AddA(IYH, true, 8);
            fdOpcodeTable[0x8D] = () => AddA(IYL, true, 8);
            fdOpcodeTable[0x8E] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IY + d)), true, 19); };

            fdOpcodeTable[0x94] = () => SubA(IYH, false, 8);
            fdOpcodeTable[0x95] = () => SubA(IYL, false, 8);
            fdOpcodeTable[0x96] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IY + d)), false, 19); };
            fdOpcodeTable[0x9C] = () => SubA(IYH, true, 8);
            fdOpcodeTable[0x9D] = () => SubA(IYL, true, 8);
            fdOpcodeTable[0x9E] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IY + d)), true, 19); };

            fdOpcodeTable[0xA4] = () => AndA(IYH, 8);
            fdOpcodeTable[0xA5] = () => AndA(IYL, 8);
            fdOpcodeTable[0xA6] = () => { sbyte d = (sbyte)FetchByte(); AndA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xAC] = () => XorA(IYH, 8);
            fdOpcodeTable[0xAD] = () => XorA(IYL, 8);
            fdOpcodeTable[0xAE] = () => { sbyte d = (sbyte)FetchByte(); XorA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xB4] = () => OrA(IYH, 8);
            fdOpcodeTable[0xB5] = () => OrA(IYL, 8);
            fdOpcodeTable[0xB6] = () => { sbyte d = (sbyte)FetchByte(); OrA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xBC] = () => CpA(IYH, 8);
            fdOpcodeTable[0xBD] = () => CpA(IYL, 8);
            fdOpcodeTable[0xBE] = () => { sbyte d = (sbyte)FetchByte(); CpA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xE1] = () => { Regs.IY = Pop(); TStates += 10; };
            fdOpcodeTable[0xE3] = () =>
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, (byte)(Regs.IY & 0xFF));
                WriteMemory((ushort)(Regs.SP + 1), (byte)(Regs.IY >> 8));
                Regs.IY = (ushort)(low | (high << 8));
                TStates += 19;
            };
            fdOpcodeTable[0xE5] = () => { Push(Regs.IY); TStates += 11; };
            fdOpcodeTable[0xE9] = () => { Regs.PC = Regs.IY; TStates += 8; };
            fdOpcodeTable[0xF9] = () => { Regs.SP = Regs.IY; TStates += 6; };
        }

        // =========================================================
        // Indexed CB
        // =========================================================

        private void ExecuteIndexedCB(ushort indexReg, sbyte disp, byte cbOp)
        {
            ushort addr = (ushort)(indexReg + disp);
            byte value = ReadMemory(addr);

            int group = (cbOp >> 6) & 0x03;
            int y = (cbOp >> 3) & 0x07;
            int z = cbOp & 0x07;

            switch (group)
            {
                case 0:
                {
                    byte result = value;
                    bool carry = false;

                    switch (y)
                    {
                        case 0: carry = (value & 0x80) != 0; result = (byte)((value << 1) | (carry ? 1 : 0)); break; // RLC
                        case 1: carry = (value & 0x01) != 0; result = (byte)((value >> 1) | (carry ? 0x80 : 0x00)); break; // RRC
                        case 2:
                        {
                            bool oldCarry = (Regs.F & 0x01) != 0;
                            carry = (value & 0x80) != 0;
                            result = (byte)((value << 1) | (oldCarry ? 1 : 0));
                            break;
                        }
                        case 3:
                        {
                            bool oldCarry = (Regs.F & 0x01) != 0;
                            carry = (value & 0x01) != 0;
                            result = (byte)((value >> 1) | (oldCarry ? 0x80 : 0x00));
                            break;
                        }
                        case 4: carry = (value & 0x80) != 0; result = (byte)(value << 1); break; // SLA
                        case 5: carry = (value & 0x01) != 0; result = (byte)((value >> 1) | (value & 0x80)); break; // SRA
                        case 6: carry = (value & 0x80) != 0; result = (byte)((value << 1) | 0x01); break; // SLL
                        case 7: carry = (value & 0x01) != 0; result = (byte)(value >> 1); break; // SRL
                    }

                    WriteMemory(addr, result);
                    SetShiftRotateFlags(result, carry);

                    if (z != 6)
                        SetReg((byte)z, result);

                    TStates += 23;
                    break;
                }

                case 1: // BIT
                {
                    bool bitSet = (value & (1 << y)) != 0;
                    SetFlag(Flag.Z, !bitSet);
                    SetFlag(Flag.N, false);
                    SetFlag(Flag.H, true);
                    SetFlag(Flag.P, !bitSet);
                    SetFlag(Flag.S, y == 7 && bitSet);
                    TStates += 20;
                    break;
                }

                case 2: // RES
                {
                    byte result = (byte)(value & ~(1 << y));
                    WriteMemory(addr, result);
                    if (z != 6)
                        SetReg((byte)z, result);
                    TStates += 23;
                    break;
                }

                case 3: // SET
                {
                    byte result = (byte)(value | (1 << y));
                    WriteMemory(addr, result);
                    if (z != 6)
                        SetReg((byte)z, result);
                    TStates += 23;
                    break;
                }
            }
        }

        // =========================================================
        // Trace
        // =========================================================

        private void RecordTrace(ushort pcBefore, byte op)
        {
            string line = $"PC={pcBefore:X4} OP={op:X2} SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4}";
            recentTrace.Enqueue(line);
            if (recentTrace.Count > 40)
                recentTrace.Dequeue();
        }

        // =========================================================
        // Register helpers
        // =========================================================

        private byte GetReg(byte idx)
        {
            return idx switch
            {
                0 => Regs.B,
                1 => Regs.C,
                2 => Regs.D,
                3 => Regs.E,
                4 => Regs.H,
                5 => Regs.L,
                6 => ReadMemory(Regs.HL),
                7 => Regs.A,
                _ => 0
            };
        }

        private void SetReg(byte idx, byte val)
        {
            switch (idx)
            {
                case 0: Regs.B = val; break;
                case 1: Regs.C = val; break;
                case 2: Regs.D = val; break;
                case 3: Regs.E = val; break;
                case 4: Regs.H = val; break;
                case 5: Regs.L = val; break;
                case 6: WriteMemory(Regs.HL, val); break;
                case 7: Regs.A = val; break;
            }
        }

        private byte Inc8(byte old)
        {
            byte value = (byte)(old + 1);
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x0F);
            SetFlag(Flag.P, old == 0x7F);
            SetFlag(Flag.N, false);
            CopyUndocumentedFlagsFrom(value);

            return value;
        }

        private byte Dec8(byte old)
        {
            byte value = (byte)(old - 1);
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x00);
            SetFlag(Flag.P, old == 0x80);
            SetFlag(Flag.N, true);
            CopyUndocumentedFlagsFrom(value);

            return value;
        }

        private void IncReg(byte idx)
        {
            byte old = GetReg(idx);
            byte val = (byte)(old + 1);
            SetReg(idx, val);

            SetFlag(Flag.S, (val & 0x80) != 0);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x0F);
            SetFlag(Flag.P, old == 0x7F);
            SetFlag(Flag.N, false);
            CopyUndocumentedFlagsFrom(val);

            TStates += (idx == 6 ? 11UL : 4UL);
        }

        private void DecReg(byte idx)
        {
            byte old = GetReg(idx);
            byte val = (byte)(old - 1);
            SetReg(idx, val);

            SetFlag(Flag.S, (val & 0x80) != 0);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x00);
            SetFlag(Flag.P, old == 0x80);
            SetFlag(Flag.N, true);
            CopyUndocumentedFlagsFrom(val);

            TStates += (idx == 6 ? 11UL : 4UL);
        }

        // =========================================================
        // ALU helpers
        // =========================================================

        private static bool OverflowAdd(byte a, byte b, byte r)
        {
            return ((a ^ r) & (b ^ r) & 0x80) != 0;
        }

        private static bool OverflowSub(byte a, byte b, byte r)
        {
            return ((a ^ b) & (a ^ r) & 0x80) != 0;
        }

        private void AddA(byte value, bool withCarry, int tstates)
        {
            int carry = withCarry && (Regs.F & (1 << (int)Flag.C)) != 0 ? 1 : 0;

            byte a = Regs.A;
            int result = a + value + carry;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a & 0x0F) + (value & 0x0F) + carry) > 0x0F);
            SetFlag(Flag.P, OverflowAdd(a, value, r));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, result > 0xFF);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)tstates;
        }

        private void SubA(byte value, bool withCarry, int tstates)
        {
            int carry = withCarry && (Regs.F & (1 << (int)Flag.C)) != 0 ? 1 : 0;

            byte a = Regs.A;
            int result = a - value - carry;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a & 0x0F) - (value & 0x0F) - carry) < 0);
            SetFlag(Flag.P, OverflowSub(a, value, r));
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)tstates;
        }

        private void AndA(byte value, int tstates)
        {
            byte r = (byte)(Regs.A & value);
            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, true);
            SetFlag(Flag.P, Parity(r));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)tstates;
        }

        private void XorA(byte value, int tstates)
        {
            byte r = (byte)(Regs.A ^ value);
            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(r));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)tstates;
        }

        private void OrA(byte value, int tstates)
        {
            byte r = (byte)(Regs.A | value);
            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(r));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)tstates;
        }

        private void CpA(byte value, int baseT)
        {
            byte a = Regs.A;
            int result = a - value;
            byte r = (byte)result;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ value ^ r) & 0x10) != 0);
            SetFlag(Flag.P, OverflowSub(a, value, r));
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);
            CopyUndocumentedFlagsFrom(r);

            TStates += (ulong)baseT;
        }

        private ushort Add16(ushort a, ushort b)
        {
            int result = a + b;
            ushort r = (ushort)result;

            SetFlag(Flag.N, false);
            SetFlag(Flag.H, ((a & 0x0FFF) + (b & 0x0FFF)) > 0x0FFF);
            SetFlag(Flag.C, result > 0xFFFF);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return (ushort)result;
        }

        private ushort Add16WithCarry(ushort a, ushort b, bool carry)
        {
            int c = carry ? 1 : 0;
            int result = a + b + c;
            ushort r = (ushort)result;

            SetFlag(Flag.S, (r & 0x8000) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a & 0x0FFF) + (b & 0x0FFF) + c) > 0x0FFF);
            SetFlag(Flag.P, (((a ^ ~b) & (a ^ r)) & 0x8000) != 0);
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, result > 0xFFFF);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return r;
        }

        private ushort Sub16(ushort a, ushort b, bool carry)
        {
            int c = carry ? 1 : 0;
            int result = a - b - c;
            ushort r = (ushort)result;

            SetFlag(Flag.S, (r & 0x8000) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ b ^ r) & 0x1000) != 0);
            SetFlag(Flag.P, (((a ^ b) & (a ^ r)) & 0x8000) != 0);
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return r;
        }

        private void NegA()
        {
            byte oldA = Regs.A;
            int result = 0 - oldA;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, (oldA & 0x0F) != 0);
            SetFlag(Flag.P, oldA == 0x80); // overflow
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, oldA != 0);

            CopyUndocumentedFlagsFrom(r);
        }

        // =========================================================
        // Flag and parity helpers
        // =========================================================

        private void SetShiftRotateFlags(byte result, bool carry)
        {
            Regs.F = 0;
            if ((result & 0x80) != 0) Regs.F |= 0x80;
            if (result == 0) Regs.F |= 0x40;
            if (Parity(result)) Regs.F |= 0x04;
            if (carry) Regs.F |= 0x01;
            Regs.F |= (byte)(result & 0x28);
        }

        private void SetInFlags(byte value)
        {
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(value));
            SetFlag(Flag.N, false);
            CopyUndocumentedFlagsFrom(value);
        }

        private bool Parity(byte value)
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((value >> i) & 1) != 0)
                    count++;
            }

            return (count & 1) == 0;
        }

        private void SetFlag(Flag f, bool set)
        {
            if (set)
                Regs.F |= (byte)(1 << (int)f);
            else
                Regs.F &= (byte)~(1 << (int)f);
        }

        // =========================================================
        // Flow helpers
        // =========================================================

        private void RETcc(bool condition)
        {
            if (condition)
            {
                Regs.PC = Pop();
                TStates += 11;
            }
            else
            {
                TStates += 5;
            }
        }

        private void CALLcc(bool condition)
        {
            ushort addr = FetchWord();
            if (condition)
            {
                Push(Regs.PC);
                Regs.PC = addr;
                TStates += 17;
            }
            else
            {
                TStates += 10;
            }
        }

        private void JRcc(bool condition)
        {
            sbyte e = (sbyte)FetchByte();
            if (condition)
                Regs.PC = (ushort)(Regs.PC + e);
            TStates += 12;
        }

        private void JPcc(bool condition)
        {
            ushort addr = FetchWord();
            if (condition)
                Regs.PC = addr;
            TStates += 10;
        }

        // =========================================================
        // Stack and fetch helpers
        // =========================================================

        private void Push(ushort value)
        {
            Regs.SP -= 2;
            WriteMemory(Regs.SP, (byte)value);
            WriteMemory((ushort)(Regs.SP + 1), (byte)(value >> 8));
        }

        private ushort Pop()
        {
            ushort value = (ushort)(ReadMemory(Regs.SP) | (ReadMemory((ushort)(Regs.SP + 1)) << 8));
            Regs.SP += 2;
            return value;
        }

        // Operand and prefix fetches do not add T-states here.
        // Instruction handlers own the full documented timing for the instruction.
        private byte FetchByte()
        {
            byte b = ReadMemory(Regs.PC);
            Regs.PC = (ushort)(Regs.PC + 1);
            Regs.R = (byte)((Regs.R & 0x80) | ((Regs.R + 1) & 0x7F));
            return b;
        }

        private ushort FetchWord()
        {
            byte low = FetchByte();
            byte high = FetchByte();
            return (ushort)(low | (high << 8));
        }
    }
}

// LD-BYTES VERIFY fix: ensure main A and carry flag used
