using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace GraphQLinq.Shared.Scaffolding
{
    public class QueriesArgs : Dictionary<string, QueryArgs>
    {
    }

    public class QueryArgs : Dictionary<string, string>
    {
    }
}