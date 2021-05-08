using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MySourceGenerator
{
    [Generator]
    public class AutoNotifyGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;
namespace AutoNotify
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class AutoNotifyAttribute : Attribute
    {
        public AutoNotifyAttribute()
        {
        }
        public string PropertyName { get; set; }
    }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
// #if DEBUG
//             if (!Debugger.IsAttached)
//             {
//                 Debugger.Launch();
//             }
// #endif 

            // 註冊一個語法接收器，會在每次生成時被建立
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // 新增 Attrbite 文字
            context.AddSource("AutoNotifyAttribute", SourceText.From(attributeText, Encoding.UTF8));

            // 獲取先前的語法接收器 
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            // 建立處目標名稱的屬性
            CSharpParseOptions options = (context.Compilation as CSharpCompilation).SyntaxTrees[0].Options as CSharpParseOptions;
            Compilation compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(attributeText, Encoding.UTF8), options));

            // 獲取新繫結的 Attribute，並獲取INotifyPropertyChanged
            INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("AutoNotify.AutoNotifyAttribute");
            INamedTypeSymbol notifySymbol = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");

            // 遍歷欄位，只保留有 AutoNotify 標註的欄位
            List<IFieldSymbol> fieldSymbols = new List<IFieldSymbol>();
            foreach (FieldDeclarationSyntax field in receiver.CandidateFields)
            {
                SemanticModel model = compilation.GetSemanticModel(field.SyntaxTree);
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    // 獲取欄位符號資訊，如果有 AutoNotify 標註則儲存
                    IFieldSymbol fieldSymbol = ModelExtensions.GetDeclaredSymbol(model, variable) as IFieldSymbol;
                    if (fieldSymbol.GetAttributes().Any(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default)))
                    {
                        fieldSymbols.Add(fieldSymbol);
                    }
                }
            }

            // 按 class 對欄位進行分組，並生成程式碼
            foreach (IGrouping<INamedTypeSymbol, IFieldSymbol> group in fieldSymbols.GroupBy(f => f.ContainingType))
            {
                string classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, notifySymbol, context);
                context.AddSource($"{group.Key.Name}_autoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, ISymbol notifySymbol, GeneratorExecutionContext context)
        {
            if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            {
                // TODO: 必須在頂層，產生診斷資訊
                return null;
            }

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // 開始構建要生成的程式碼
            StringBuilder source = new StringBuilder($@"
namespace {namespaceName}
{{
    public partial class {classSymbol.Name} : {notifySymbol.ToDisplayString()}
    {{
");

            // 如果型別還沒有實現 INotifyPropertyChanged 則新增實現
            if (!classSymbol.Interfaces.Contains(notifySymbol))
            {
                source.Append("public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;");
            }

            // 生成屬性
            foreach (IFieldSymbol fieldSymbol in fields)
            {
                ProcessField(source, fieldSymbol, attributeSymbol);
            }

            source.Append("} }");
            return source.ToString();
        }

        private void ProcessField(StringBuilder source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
        {
            // 獲取欄位名稱
            string fieldName = fieldSymbol.Name;
            ITypeSymbol fieldType = fieldSymbol.Type;

            // 獲取 AutoNotify Attribute 和相關的資料
            AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
            TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

            string propertyName = chooseName(fieldName, overridenNameOpt);
            if (propertyName.Length == 0 || propertyName == fieldName)
            {
                //TODO: 無法處理，產生診斷資訊
                return;
            }

            source.Append($@"
public {fieldType} {propertyName} 
{{
    get 
    {{
        return this.{fieldName};
    }}
    set
    {{
        this.{fieldName} = value;
        this.PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof({propertyName})));
    }}
}}
");


        }
        
        string chooseName(string fieldName, TypedConstant overridenNameOpt)
        {
            if (!overridenNameOpt.IsNull)
            {
                return overridenNameOpt.Value.ToString();
            }

            fieldName = fieldName.TrimStart('_');
            if (fieldName.Length == 0)
                return string.Empty;

            if (fieldName.Length == 1)
                return fieldName.ToUpper();

            return fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);
        }

        // 語法接收器，將在每次生成程式碼時被按需建立
        class SyntaxReceiver : ISyntaxReceiver
        {
            public List<FieldDeclarationSyntax> CandidateFields { get; } = new List<FieldDeclarationSyntax>();

            // 編譯中在訪問每個語法節點時被呼叫，我們可以檢查節點並儲存任何對生成有用的資訊
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // 將具有至少一個 Attribute 的任何欄位作為候選
                if (syntaxNode is FieldDeclarationSyntax fieldDeclarationSyntax
                    && fieldDeclarationSyntax.AttributeLists.Count > 0)
                {
                    CandidateFields.Add(fieldDeclarationSyntax);
                }
            }
        }
    }
}