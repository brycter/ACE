using ACE.Entity;

internal static class BenchmarkRooms
{
    // Stock portal arrival positions, one distinct dungeon landblock per group.
    internal static readonly Position[] Arrivals = {
        new(0x01D3010F, 20.5f, -10.3f, 0f, 0f, 0f, -0.690882f, -0.722967f), // 00368 Zabool Tower Base
        new(0x01D601B9, 135.8f, -1.22f, 0f, 0f, 0f, -0.692143f, -0.72176f), // 00371 Alfreth Dungeon
        new(0x01D40115, 40f, -30f, 0f, 0f, 0f, -0.690882f, -0.722967f), // 00373 Bellig Tower Base
        new(0x01D501C0, 39.093f, -13.057f, 66f, 0f, 0f, -0.975724f, -0.219003f), // 00426 Syliph Tower
        new(0x01D70277, 59.8f, -136.9f, 0f, 0f, 0f, -0.884581f, -0.466386f), // 00428 Sylsfear Dungeon Portal
        new(0x01EF03DD, 67.5f, -69.8f, 0f, 0f, 0f, -0.707107f, -0.707107f), // 01088 Arwic Mines Portal
        new(0x01E901AD, 120f, -130f, -11.995f, 0f, 0f, -0.699713f, -0.714424f), // 01099 Underground City Portal
        new(0x01EA041E, 60f, -60f, 54f, 0f, 0f, -0.945519f, -0.325568f), // 01116 Old Talisman Dungeon Portal
        new(0x01ED0310, 120f, 0f, 12f, 0f, 0f, 0f, 1f), // 01117 Dungeon of Shadows Portal
        new(0x01F90101, 0f, -130f, -18f, 0f, 0f, -0.422618f, 0.906308f), // 01119 Accursed Halls Portal
        new(0x01FA034C, 80f, -59f, 6f, 0f, 0f, -0.346117f, 0.938191f), // 01120 Crypt of Ashen Tears
        new(0x01F801D4, 6.1f, -101.6f, 0f, 0f, 0f, -0.669131f, 0.743145f), // 01121 Mite Maze Portal
        new(0x01FD02B5, 88.76f, -59.916f, 12f, 0f, 0f, -0.682573f, 0.730818f), // 01122 Trothyr's Rest
        new(0x01E801BA, -0.6f, -38.5f, 0f, 0f, 0f, -0.997564f, 0.069757f), // 01123 Dungeon Manor Portal
        new(0x01F701ED, 40f, -140f, 0f, 0f, 0f, 0f, 1f), // 01124 Shoushi Grotto Portal
        new(0x01FB0206, 92f, -20f, 0f, 0f, 0f, -0.707107f, 0.707107f), // 01126 Yaraq Tunnels Portal
        new(0x01FC0114, 18.9f, -58.9f, 0f, 0f, 0f, -0.771625f, 0.636078f), // 01127 ReedShark Lair Portal
        new(0x01E10272, 88.46f, -69.724f, 0f, 0f, 0f, -0.709319f, 0.704888f), // 01219 Thieves' Den
        new(0x01E20231, 71.894f, -20f, 24.005f, 0f, 0f, -0.685806f, -0.727785f), // 01306 Bandit Castle Prison
        new(0x01E3033D, 161f, -150f, 0f, 0f, 0f, -0.182236f, -0.983255f), // 01307 Glenden Wood Dungeon
        new(0x01E5020E, 80.915f, -69.263f, 0f, 0f, 0f, -0.019198f, 0.999816f), // 01309 Green Mire Grave
        new(0x01E401AD, 38.4f, -18.6f, 6f, 0f, 0f, -0.622515f, -0.782608f), // 01311 North Glenden Prison
        new(0x01CF0237, 119.625f, -163.107f, 6.005f, 0f, 0f, -0.01391f, -0.999903f), // 01323 Eastham Sewer
        new(0x01E70121, 58.561f, -28.65f, 0f, 0f, 0f, -0.833886f, -0.551937f), // 01325 A Small Cave
        new(0x01E60250, 140f, -190f, 6.1f, 0f, 0f, 0f, 1f), // 01327 Small Icecave
        new(0x01E00121, 13.0085f, -27.0157f, 6.005f, 0f, 0f, -1f, 0f), // 01329 Banderling Ruin
        new(0x01F1010A, 20f, -40f, 0f, 0f, 0f, -0.737277f, 0.67559f), // 01331 Cave
        new(0x01DF0106, 7.1f, -28.8f, 0.5f, 0f, 0f, -0.294207f, 0.955742f), // 01333 A Mosswart Nest
        new(0x01D80108, 21.4f, -1.3f, 0f, 0f, 0f, -0.97237f, 0.233445f), // 01335 A Drudge Nest
        new(0x01DC015C, 32.3f, -12f, 6f, 0f, 0f, -0.377033f, -0.9262f), // 01337 A Drudge Nest
        new(0x01DB01DC, 60f, -60f, 0f, 0f, 0f, -0.927184f, 0.374607f), // 01339 Moss Chamber
        new(0x01DE0195, 30.1f, -8.2f, 0f, 0f, 0f, -0.999863f, -0.01658f), // 01341 A Ruin
        new(0x01DA010F, 11.4f, -21.6f, 0f, 0f, 0f, -0.71325f, -0.700909f), // 01343 A Ruin
        new(0x01D90100, 3f, -49.9f, 0f, 0f, 0f, -0.707107f, 0.707107f), // 01345 A Red Rat Lair
        new(0x01DD015F, 30.1f, -66.6f, 0f, 0f, 0f, -0.02007f, 0.999799f), // 01347 A Rat Nest
        new(0x01D202F7, 47.9f, -69.9f, 12f, 0f, 0f, -0.69904f, 0.715083f), // 01364 Mountain Keep
    };
}
