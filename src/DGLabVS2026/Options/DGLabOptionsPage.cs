using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace DGLab.VS2026
{
    /// <summary>
    /// Tools > Options > DGLab > 郊狼插件配置。
    /// 对应原 VSCode 插件的 dglabvscode.* 设置项。
    /// </summary>
    [Guid(GuidList.OptionsPageString)]
    [ComVisible(true)]
    public sealed class DGLabOptionsPage : DialogPage
    {
        [Category("连接")]
        [DisplayName("基础强度")]
        [Description("连接后下发到 A/B 两个通道的基础强度。")]
        [DefaultValue(5)]
        public int Strength { get; set; } = 5;

        [Category("连接")]
        [DisplayName("波形名称")]
        [Description("默认波形名称。可选值见“设置波形名称”命令；默认：快速按捏。")]
        [DefaultValue("快速按捏")]
        public string PulseName { get; set; } = "快速按捏";

        [Category("连接")]
        [DisplayName("心跳间隔（秒）")]
        [Description("心跳与强度渐增的间隔秒数。小于 10 秒可能导致快速被电。")]
        [DefaultValue(30)]
        public int HeartbeatInterval { get; set; } = 30;

        [Category("连接")]
        [DisplayName("最大强度渐增")]
        [Description("心跳时基础强度最多增加多少。对应原 maxAddInterval。")]
        [DefaultValue(20)]
        public int MaxAddInterval { get; set; } = 20;

        [Category("事件强度")]
        [DisplayName("开始调试时开火")]
        [Description("开始调试会话时临时提升的强度；0 表示仍然发送但不额外提升。")]
        [DefaultValue(15)]
        public int OnStartDebugSession { get; set; } = 15;

        [Category("事件强度")]
        [DisplayName("结束调试时开火")]
        [Description("结束调试会话时临时提升的强度。")]
        [DefaultValue(15)]
        public int OnTerminateDebugSession { get; set; } = 15;

        [Category("事件强度")]
        [DisplayName("调试报错时开火")]
        [Description("调试过程中因异常中断时开火（对应原 stderr 报错惩罚）。")]
        [DefaultValue(20)]
        public int OnDebugError { get; set; } = 20;

        [Category("事件强度")]
        [DisplayName("断点变化时开火")]
        [Description("添加、删除、启用或禁用断点时开火。")]
        [DefaultValue(10)]
        public int OnBreakpointsChanged { get; set; } = 10;

        [Category("事件强度")]
        [DisplayName("保存文件时开火")]
        [Description("保存文件时开火。")]
        [DefaultValue(12)]
        public int OnSaveDocument { get; set; } = 12;

        [Category("编辑")]
        [DisplayName("编辑事件上限（次/20秒）")]
        [Description("20 秒内编辑事件超过该数量时清空输出队列，防止手速过快被电。对应原 condingMaxfFrequency。")]
        [DefaultValue(20)]
        public int CodingMaxFrequency { get; set; } = 20;

        [Category("输出")]
        [DisplayName("输出通道")]
        [Description("选择仅 A、仅 B 或 AB 一起输出。")]
        [DefaultValue(ChannelMode.AB)]
        public ChannelMode MessageSendOption { get; set; } = ChannelMode.AB;

        [Category("黑名单")]
        [DisplayName("黑名单后缀名")]
        [Description("多个后缀名用逗号、分号或空格分隔，不需要点。例如：json;md;txt。这些文件不会触发编辑/保存事件。")]
        [DefaultValue("")]
        public string Blacklist { get; set; } = string.Empty;

        public DGLabSettings ToSettings()
        {
            return new DGLabSettings(
                Strength,
                PulseName,
                HeartbeatInterval,
                MaxAddInterval,
                OnStartDebugSession,
                OnTerminateDebugSession,
                OnDebugError,
                OnBreakpointsChanged,
                OnSaveDocument,
                CodingMaxFrequency,
                MessageSendOption,
                ParseBlacklist(Blacklist));
        }

        public static IEnumerable<string> ParseBlacklist(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Enumerable.Empty<string>();
            }

            return text
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('.'))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnApply(e);

            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                DGLabPackage.Instance?.OnOptionsChanged();
            }
        }
    }
}
