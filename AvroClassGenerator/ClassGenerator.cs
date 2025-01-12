using System.IO;
using System.Linq;
using System.Text;
using Chr.Avro.Abstract;
using Chr.Avro.Representation;
using Microsoft.CodeAnalysis;

namespace AvroClassGenerator;

[Generator]
public class ClassGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var avroFiles = context
            .AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".avsc"))
            .Select(static (file, ct) =>
            {
                var fileName = Path.GetFileNameWithoutExtension(file.Path);
                var fileContent = file.GetText(ct);

                if (fileContent is null)
                    return null;

                return new AvroFile(fileName, fileContent.ToString());
            });

        context.RegisterSourceOutput(avroFiles, GenerateWithAvro);
    }

    private static void GenerateWithAvro(SourceProductionContext context, AvroFile? avroFile)
    {
        if (avroFile is null)
            return;

        var jsonSchemaReader = new JsonSchemaReader();
        if (jsonSchemaReader.Read(avroFile.Content) is not RecordSchema schema)
            return;

        var namespaceName = StringConverter.ConvertToNamespaceCase(schema.Namespace ?? "");
        var className = StringConverter.ConvertToPascalCase(schema.Name);
        var fields = schema.Fields;

        if (string.IsNullOrWhiteSpace(namespaceName) || fields.Count == 0)
            return;

        var fileName = $"{namespaceName}.{className}.g.cs";

        var stringBuilder = new StringBuilder();

        var header = $$"""
                       #nullable enable
                       using System.Runtime.Serialization;

                       namespace {{namespaceName}};

                       [DataContract(Name = "{{schema.Name}}", Namespace = "{{schema.Namespace}}")]
                       public record {{className}}
                       {
                       """;

        stringBuilder.AppendLine(header);

        foreach (var field in fields)
        {
            var fieldName = field.Name;
            var fieldNameInPascalCase = StringConverter.ConvertToPascalCase(fieldName);

            switch (field.Type)
            {
                case UnionSchema unionSchema when unionSchema.Schemas.Any(static s => s is NullSchema):
                {
                    var primaryType = unionSchema.Schemas.FirstOrDefault(static s => s is not NullSchema);
                    if (primaryType is StringSchema)
                    {
                        var property = GenerateStringProperty(fieldName, fieldNameInPascalCase, true, field.Default);
                        stringBuilder.AppendLine(property);
                    }

                    if (primaryType is BooleanSchema)
                    {
                        var property = GenerateBooleanProperty(fieldName, fieldNameInPascalCase, true, field.Default);
                        stringBuilder.AppendLine(property);
                    }

                    break;
                }
                case StringSchema:
                {
                    var property = GenerateStringProperty(fieldName, fieldNameInPascalCase, false, field.Default);
                    stringBuilder.AppendLine(property);
                    break;
                }
                case BooleanSchema:
                {
                    var property = GenerateBooleanProperty(fieldName, fieldNameInPascalCase, false, field.Default);
                    stringBuilder.AppendLine(property);
                    break;
                }
            }

            stringBuilder.AppendLine();
        }

        stringBuilder.AppendLine("}");

        context.AddSource(fileName, stringBuilder.ToString());
    }

    private static string GenerateStringProperty(string fieldName, string fieldNameInPascalCase, bool isNullable, DefaultValue? defaultValue)
    {
        var propertyTypeName = isNullable ? "string?" : "string";

        if (defaultValue is not null)
        {
            return $$"""
                        [DataMember(Name = "{{fieldName}}")]
                        public {{propertyTypeName}} {{fieldNameInPascalCase}} { get; init; } = "{{defaultValue.ToObject<string>()}}";
                    """;
        }

        return $$"""
                    [DataMember(Name = "{{fieldName}}")]
                    public required {{propertyTypeName}} {{fieldNameInPascalCase}} { get; init; }
                 """;
    }

    private static string GenerateBooleanProperty(string fieldName, string fieldNameInPascalCase, bool isNullable, DefaultValue? defaultValue)
    {
        var propertyTypeName = isNullable ? "bool?" : "bool";

        if (defaultValue is not null)
        {
            return $$"""
                        [DataMember(Name = "{{fieldName}}")]
                        public {{propertyTypeName}} {{fieldNameInPascalCase}} { get; init; } = {{defaultValue.ToObject<bool>().ToString().ToLower()}};
                     """;
        }

        return $$"""
                    [DataMember(Name = "{{fieldName}}")]
                    public required {{propertyTypeName}} {{fieldNameInPascalCase}} { get; init; }
                 """;
    }
}

public record AvroFile
{
    public string Name { get; }
    public string Content { get; }

    public AvroFile(string name, string content)
    {
        Name = name;
        Content = content;
    }
}
