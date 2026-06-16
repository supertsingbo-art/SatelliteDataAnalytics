namespace SatelliteData.Application.Templates;

public enum ConditionTokenType
{
    Identifier,
    And,
    Or,
    LeftParen,
    RightParen
}

public sealed record ConditionExpressionToken(ConditionTokenType Type, string Value);

public static class ConditionExpressionParser
{
    public static bool TryParseToPostfix(
        string expression,
        out IReadOnlyList<ConditionExpressionToken> postfix,
        out string error)
    {
        postfix = Array.Empty<ConditionExpressionToken>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        if (!TryTokenize(expression, out var tokens, out error))
        {
            return false;
        }

        if (!TryToPostfix(tokens, out var rpn, out error))
        {
            return false;
        }

        postfix = rpn;
        return true;
    }

    private static bool TryTokenize(
        string expression,
        out List<ConditionExpressionToken> tokens,
        out string error)
    {
        tokens = new List<ConditionExpressionToken>();
        error = string.Empty;

        var i = 0;
        while (i < expression.Length)
        {
            var ch = expression[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(new ConditionExpressionToken(ConditionTokenType.LeftParen, "("));
                i++;
                continue;
            }

            if (ch == ')')
            {
                tokens.Add(new ConditionExpressionToken(ConditionTokenType.RightParen, ")"));
                i++;
                continue;
            }

            if (ch == '&' && i + 1 < expression.Length && expression[i + 1] == '&')
            {
                tokens.Add(new ConditionExpressionToken(ConditionTokenType.And, "&&"));
                i += 2;
                continue;
            }

            if (ch == '|' && i + 1 < expression.Length && expression[i + 1] == '|')
            {
                tokens.Add(new ConditionExpressionToken(ConditionTokenType.Or, "||"));
                i += 2;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new ConditionExpressionToken(
                    ConditionTokenType.Identifier,
                    expression[start..i]));
                continue;
            }

            error = $"表达式包含非法字符：'{ch}'";
            return false;
        }

        if (tokens.Count == 0)
        {
            error = "表达式为空";
            return false;
        }

        return true;
    }

    private static bool TryToPostfix(
        IReadOnlyList<ConditionExpressionToken> tokens,
        out IReadOnlyList<ConditionExpressionToken> postfix,
        out string error)
    {
        var output = new List<ConditionExpressionToken>();
        var operators = new Stack<ConditionExpressionToken>();
        error = string.Empty;
        postfix = Array.Empty<ConditionExpressionToken>();

        var expectOperand = true;
        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case ConditionTokenType.Identifier:
                    if (!expectOperand)
                    {
                        error = $"表达式在标识符 '{token.Value}' 前缺少逻辑运算符";
                        return false;
                    }

                    output.Add(token);
                    expectOperand = false;
                    break;
                case ConditionTokenType.LeftParen:
                    if (!expectOperand)
                    {
                        error = "表达式中 '(' 前缺少逻辑运算符";
                        return false;
                    }

                    operators.Push(token);
                    break;
                case ConditionTokenType.RightParen:
                    if (expectOperand)
                    {
                        error = "表达式中 ')' 位置非法";
                        return false;
                    }

                    var matched = false;
                    while (operators.Count > 0)
                    {
                        var op = operators.Pop();
                        if (op.Type == ConditionTokenType.LeftParen)
                        {
                            matched = true;
                            break;
                        }

                        output.Add(op);
                    }

                    if (!matched)
                    {
                        error = "表达式括号不匹配：存在多余的 ')'";
                        return false;
                    }

                    expectOperand = false;
                    break;
                case ConditionTokenType.And:
                case ConditionTokenType.Or:
                    if (expectOperand)
                    {
                        error = $"表达式在运算符 '{token.Value}' 前缺少条件项";
                        return false;
                    }

                    while (operators.Count > 0
                           && operators.Peek().Type is ConditionTokenType.And or ConditionTokenType.Or
                           && Precedence(operators.Peek()) >= Precedence(token))
                    {
                        output.Add(operators.Pop());
                    }

                    operators.Push(token);
                    expectOperand = true;
                    break;
                default:
                    error = $"不支持的 token: {token.Type}";
                    return false;
            }
        }

        if (expectOperand)
        {
            error = "表达式不能以逻辑运算符结尾";
            return false;
        }

        while (operators.Count > 0)
        {
            var op = operators.Pop();
            if (op.Type == ConditionTokenType.LeftParen)
            {
                error = "表达式括号不匹配：存在未闭合的 '('";
                return false;
            }

            output.Add(op);
        }

        postfix = output;
        return true;
    }

    public static bool ValidateIdentifiers(
        IReadOnlyList<ConditionExpressionToken> postfix,
        IReadOnlySet<string> allowedIds,
        out string error)
    {
        foreach (var token in postfix)
        {
            if (token.Type == ConditionTokenType.Identifier
                && !allowedIds.Contains(token.Value))
            {
                error = $"表达式引用了未定义条件ID：{token.Value}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static int Precedence(ConditionExpressionToken token)
    {
        return token.Type switch
        {
            ConditionTokenType.And => 2,
            ConditionTokenType.Or => 1,
            _ => 0
        };
    }
}
