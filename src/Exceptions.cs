﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GEngine
{
    public class EngineException : Exception
    {
        public string SourceFile { get; set; }
        public EngineException() : base()
        {
            SourceFile = "n/a";
        }
        public EngineException(string msg) : base(msg)
        {
            SourceFile = "n/a";
        }
        public EngineException(string msg, string source) : base(msg)
        {
            SourceFile = source;
        }
    }
    public class ResourceException : EngineException
    {
        public new string SourceFile { get; set; }
        public ResourceException() : base()
        {
            SourceFile = "n/a";
        }
        public ResourceException(string msg) : base(msg)
        {
            SourceFile = "n/a";
        }
        public ResourceException(string msg, string source) : base(msg)
        {
            SourceFile = source;
        }
    }
}
