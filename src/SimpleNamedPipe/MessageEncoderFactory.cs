using System;

namespace SimpleNamedPipe;

/// <summary>
/// 消息编码器工厂类
/// </summary>
public static class MessageEncoderFactory
{
    /// <summary>
    /// 根据传输模式创建对应的消息编码器
    /// </summary>
    /// <param name="transmissionMode">传输模式</param>
    /// <returns>消息编码器实例</returns>
    /// <exception cref="PlatformNotSupportedException">当在非Windows平台使用MessageBased模式时抛出</exception>
    /// <exception cref="ArgumentOutOfRangeException">当传输模式无效时抛出</exception>
    public static IMessageEncoder CreateEncoder(MessageTransmissionMode transmissionMode)
    {
        if (transmissionMode == MessageTransmissionMode.MessageBased && Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw new PlatformNotSupportedException("MessageBasedEncoder (PipeTransmissionMode.Message) is only supported on Windows.");
        }

        return transmissionMode switch
        {
            MessageTransmissionMode.MessageBased => new MessageBasedEncoder(),
            MessageTransmissionMode.ByteBasedLittleEndian => new ByteBasedEncoder(isLittleEndian: true),
            MessageTransmissionMode.ByteBasedBigEndian => new ByteBasedEncoder(isLittleEndian: false),
            MessageTransmissionMode.BinaryFormatterCompatibleLittleEndian => new BinaryFormatterCompatibleEncoder(isLittleEndian: true),
            MessageTransmissionMode.BinaryFormatterCompatibleBigEndian => new BinaryFormatterCompatibleEncoder(isLittleEndian: false),
            _ => throw new ArgumentOutOfRangeException(nameof(transmissionMode), transmissionMode, "Invalid transmission mode")
        };
    }
} 