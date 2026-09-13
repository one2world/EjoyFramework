//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏框架异常类。
    /// </summary>
    [Serializable]
    public class FrameworkException : Exception
    {
        public FrameworkException()
            : base()
        {
        }

        public FrameworkException(string message)
            : base(message)
        {
        }

        public FrameworkException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected FrameworkException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
