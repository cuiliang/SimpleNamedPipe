namespace SimpleNamedPipe;

/// <summary>
/// 消息传输模式枚举
/// </summary>
public enum MessageTransmissionMode
{
    /// <summary>
    /// 基于消息的传输模式（仅限Windows）
    /// </summary>
    MessageBased,
    
    /// <summary>
    /// 基于字节流的传输模式，使用小端字节序存储消息长度
    /// </summary>
    ByteBasedLittleEndian,
    
    /// <summary>
    /// 基于字节流的传输模式，使用大端字节序存储消息长度
    /// </summary>
    ByteBasedBigEndian,
    
    /// <summary>
    /// 兼容BinaryFormatter格式的传输模式，使用小端字节序存储消息长度
    /// </summary>
    BinaryFormatterCompatibleLittleEndian,
    
    /// <summary>
    /// 兼容BinaryFormatter格式的传输模式，使用大端字节序存储消息长度
    /// </summary>
    BinaryFormatterCompatibleBigEndian
} 