using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace InstructionTemplateSpecification;

/// <summary>
/// Evaluates conditional expressions with the specification's operators
/// (&amp;&amp;, ||, !, comparisons, in / not in, chained comparisons, array
/// literals) plus and/or/not equivalents, against the template variables.
/// </summary>
internal sealed class ConditionalEvaluator
{
    private readonly CompilerOptions _options;
    private readonly VariableProcessor _variables;

    public ConditionalEvaluator(CompilerOptions options, VariableProcessor variables)
    {
        _options = options;
        _variables = variables;
    }

    public bool Evaluate(string expression, JsonObject variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ItsConditionalException("Empty conditional expression");
        }
        if (expression.Length > _options.MaxExpressionLength)
        {
            throw new ItsConditionalException($"Expression too long: {expression.Length} characters");
        }
        try
        {
            var parser = new Parser(Tokenizer.Tokenize(expression), _variables, variables);
            var result = parser.ParseExpression();
            parser.ExpectEnd();
            return IsTruthy(result);
        }
        catch (ItsCompilationException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ItsConditionalException($"Error evaluating condition '{expression}': {error.Message}");
        }
    }

    internal static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        double number => number != 0,
        string text => text.Length > 0,
        List<object?> list => list.Count > 0,
        Dictionary<string, object?> map => map.Count > 0,
        _ => true,
    };

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Operator,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static class Tokenizer
    {
        public static List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < input.Length)
            {
                var c = input[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                    tokens.Add(new Token(TokenKind.Identifier, input[start..i]));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    var start = i;
                    while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.')) i++;
                    tokens.Add(new Token(TokenKind.Number, input[start..i]));
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    var quote = c;
                    i++;
                    var builder = new StringBuilder();
                    while (i < input.Length && input[i] != quote)
                    {
                        if (input[i] == '\\' && i + 1 < input.Length)
                        {
                            i++;
                        }
                        builder.Append(input[i]);
                        i++;
                    }
                    if (i >= input.Length)
                    {
                        throw new ItsConditionalException("Unterminated string literal in condition");
                    }
                    i++;
                    tokens.Add(new Token(TokenKind.String, builder.ToString()));
                    continue;
                }
                var two = i + 1 < input.Length ? input.Substring(i, 2) : "";
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    tokens.Add(new Token(TokenKind.Operator, two));
                    i += 2;
                    continue;
                }
                if (c is '<' or '>' or '!' or '(' or ')' or '[' or ']' or ',' or '.' or '-' or '+')
                {
                    tokens.Add(new Token(TokenKind.Operator, c.ToString()));
                    i++;
                    continue;
                }
                throw new ItsConditionalException($"Unexpected character '{c}' in condition");
            }
            tokens.Add(new Token(TokenKind.End, ""));
            return tokens;
        }
    }

    private sealed class Parser
    {
        private static readonly HashSet<string> ComparisonOperators = new() { "==", "!=", "<", "<=", ">", ">=", "in", "not in" };

        private readonly List<Token> _tokens;
        private readonly VariableProcessor _resolver;
        private readonly JsonObject _variables;
        private int _position;

        public Parser(List<Token> tokens, VariableProcessor resolver, JsonObject variables)
        {
            _tokens = tokens;
            _resolver = resolver;
            _variables = variables;
        }

        private Token Current => _tokens[_position];

        private bool MatchOperator(string text)
        {
            if (Current.Kind == TokenKind.Operator && Current.Text == text)
            {
                _position++;
                return true;
            }
            return false;
        }

        private bool MatchKeyword(string word)
        {
            if (Current.Kind == TokenKind.Identifier && Current.Text == word)
            {
                _position++;
                return true;
            }
            return false;
        }

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
            {
                throw new ItsConditionalException($"Unexpected token '{Current.Text}' in condition");
            }
        }

        public object? ParseExpression() => ParseOr();

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (MatchOperator("||") || MatchKeyword("or"))
            {
                var right = ParseAnd();
                left = IsTruthy(left) || IsTruthy(right);
            }
            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseNot();
            while (MatchOperator("&&") || MatchKeyword("and"))
            {
                var right = ParseNot();
                left = IsTruthy(left) && IsTruthy(right);
            }
            return left;
        }

        private object? ParseNot()
        {
            if (MatchOperator("!") || MatchKeyword("not"))
            {
                return !IsTruthy(ParseNot());
            }
            return ParseComparison();
        }

        private object? ParseComparison()
        {
            var left = ParseUnary();
            var result = true;
            var compared = false;
            while (TryReadComparisonOperator(out var op))
            {
                var right = ParseUnary();
                result = result && Compare(left, op, right);
                compared = true;
                left = right;
            }
            return compared ? result : left;
        }

        private bool TryReadComparisonOperator(out string op)
        {
            op = "";
            if (Current.Kind == TokenKind.Operator && ComparisonOperators.Contains(Current.Text))
            {
                op = Current.Text;
                _position++;
                return true;
            }
            if (Current.Kind == TokenKind.Identifier && Current.Text == "in")
            {
                _position++;
                op = "in";
                return true;
            }
            if (Current.Kind == TokenKind.Identifier && Current.Text == "not"
                && _tokens[_position + 1].Kind == TokenKind.Identifier && _tokens[_position + 1].Text == "in")
            {
                _position += 2;
                op = "not in";
                return true;
            }
            return false;
        }

        private object? ParseUnary()
        {
            if (MatchOperator("-"))
            {
                var value = ParseUnary();
                if (value is double number) return -number;
                throw new ItsConditionalException("Unary minus applied to a non-number");
            }
            if (MatchOperator("+"))
            {
                var value = ParseUnary();
                if (value is double number) return number;
                throw new ItsConditionalException("Unary plus applied to a non-number");
            }
            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    _position++;
                    return double.Parse(token.Text, CultureInfo.InvariantCulture);
                case TokenKind.String:
                    _position++;
                    return token.Text;
                case TokenKind.Identifier:
                    return token.Text switch
                    {
                        "true" or "True" => Consume(true),
                        "false" or "False" => Consume(false),
                        "null" or "None" => Consume<object?>(null),
                        _ => ParseIdentifierPath(),
                    };
                case TokenKind.Operator when token.Text == "(":
                    _position++;
                    var inner = ParseExpression();
                    if (!MatchOperator(")"))
                    {
                        throw new ItsConditionalException("Expected ) in condition");
                    }
                    return inner;
                case TokenKind.Operator when token.Text == "[":
                    return ParseArrayLiteral();
                default:
                    throw new ItsConditionalException($"Unexpected token '{token.Text}' in condition");
            }
        }

        private object? Consume<T>(T value)
        {
            _position++;
            return value;
        }

        private object? ParseArrayLiteral()
        {
            _position++; // [
            var items = new List<object?>();
            if (MatchOperator("]"))
            {
                return items;
            }
            while (true)
            {
                items.Add(ParseExpression());
                if (MatchOperator("]"))
                {
                    return items;
                }
                if (!MatchOperator(","))
                {
                    throw new ItsConditionalException("Expected , or ] in array literal");
                }
            }
        }

        private object? ParseIdentifierPath()
        {
            var builder = new StringBuilder(Current.Text);
            _position++;
            while (true)
            {
                if (Current.Kind == TokenKind.Operator && Current.Text == "."
                    && _tokens[_position + 1].Kind == TokenKind.Identifier)
                {
                    builder.Append('.').Append(_tokens[_position + 1].Text);
                    _position += 2;
                    continue;
                }
                if (Current.Kind == TokenKind.Operator && Current.Text == "["
                    && _tokens[_position + 1].Kind == TokenKind.Number
                    && _tokens[_position + 2] is { Kind: TokenKind.Operator, Text: "]" })
                {
                    builder.Append('[').Append(_tokens[_position + 1].Text).Append(']');
                    _position += 3;
                    continue;
                }
                if (Current.Kind == TokenKind.Operator && Current.Text == "["
                    && _position + 3 < _tokens.Count
                    && _tokens[_position + 1] is { Kind: TokenKind.Operator, Text: "-" }
                    && _tokens[_position + 2].Kind == TokenKind.Number
                    && _tokens[_position + 3] is { Kind: TokenKind.Operator, Text: "]" })
                {
                    builder.Append("[-").Append(_tokens[_position + 2].Text).Append(']');
                    _position += 4;
                    continue;
                }
                break;
            }
            var resolved = _resolver.ResolveReference(builder.ToString(), _variables);
            return ToClrValue(resolved);
        }

        private static object? ToClrValue(JsonNode? node) => node switch
        {
            null => null,
            JsonArray array => array.Select(ToClrValue).ToList(),
            JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => ToClrValue(pair.Value)),
            JsonValue value => value.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number =>
                    double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture),
                System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => value.ToJsonString(),
            },
            _ => node.ToJsonString(),
        };

        private static bool Compare(object? left, string op, object? right)
        {
            switch (op)
            {
                case "==":
                    return LooseEquals(left, right);
                case "!=":
                    return !LooseEquals(left, right);
                case "in":
                    return Contains(right, left);
                case "not in":
                    return !Contains(right, left);
            }

            if (left is double a && right is double b)
            {
                return op switch
                {
                    "<" => a < b,
                    "<=" => a <= b,
                    ">" => a > b,
                    ">=" => a >= b,
                    _ => throw new ItsConditionalException($"Unsupported operator {op}"),
                };
            }
            if (left is string sa && right is string sb)
            {
                var comparison = string.CompareOrdinal(sa, sb);
                return op switch
                {
                    "<" => comparison < 0,
                    "<=" => comparison <= 0,
                    ">" => comparison > 0,
                    ">=" => comparison >= 0,
                    _ => throw new ItsConditionalException($"Unsupported operator {op}"),
                };
            }
            throw new ItsConditionalException($"Cannot compare values with '{op}' in condition");
        }

        private static bool LooseEquals(object? left, object? right)
        {
            if (left is null && right is null) return true;
            if (left is double a && right is double b) return a == b;
            if (left is string sa && right is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
            if (left is bool ba && right is bool bb) return ba == bb;
            if (left is List<object?> la && right is List<object?> lb)
            {
                return la.Count == lb.Count && la.Zip(lb).All(pair => LooseEquals(pair.First, pair.Second));
            }
            return false;
        }

        private static bool Contains(object? container, object? item)
        {
            return container switch
            {
                List<object?> list => list.Any(entry => LooseEquals(entry, item)),
                string text when item is string needle => text.Contains(needle, StringComparison.Ordinal),
                _ => throw new ItsConditionalException("The in operator requires an array or string on the right"),
            };
        }
    }
}
