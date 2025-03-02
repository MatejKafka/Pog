using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace Pog.Utils;

internal static class AstExtensions {
    public static IEnumerable<T> FindAllByType<T>(this Ast ast, bool searchNestedScriptBlocks) where T : Ast {
        return ast.FindAll(node => node is T, searchNestedScriptBlocks).Cast<T>();
    }
}
