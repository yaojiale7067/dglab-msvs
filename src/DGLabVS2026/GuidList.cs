using System;

namespace DGLab.VS2026
{
    internal static class GuidList
    {
        public const string PackageString = "7f3c1d2e-9a4b-4c5d-8e6f-0a1b2c3d4e5f";
        public const string CommandSetString = "c4a8e2b1-5d3f-4a7c-9b6e-1f2d3c4b5a69";
        public const string ToolWindowString = "2b7e4c1a-8d5f-4e3b-9c6a-7f1d2e3c4b5a";
        public const string OptionsPageString = "9d2c4b1e-6f3a-4b5c-8d7e-2a1f0c9b8d7e";
        public const string OutputPaneString = "5e1f3a2b-7c4d-4e8f-9a6b-3c2d1e0f9a8b";

        public static readonly Guid CommandSet = new Guid(CommandSetString);
    }

    internal static class CommandIds
    {
        public const int Connect = 0x0100;
        public const int Refresh = 0x0101;
        public const int SetPulseName = 0x0102;
        public const int ShowQr = 0x0103;
    }
}
