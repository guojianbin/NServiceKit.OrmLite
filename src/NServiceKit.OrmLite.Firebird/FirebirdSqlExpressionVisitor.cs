using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace NServiceKit.OrmLite.Firebird
{
    /// <summary>A firebird SQL expression visitor.</summary>
    /// <typeparam name="T">Generic type parameter.</typeparam>
	public class FirebirdSqlExpressionVisitor<T>:SqlExpressionVisitor<T>
	{
        /// <summary>The true expression.</summary>
	    private readonly string _trueExpression;

        /// <summary>The false expression.</summary>
	    private readonly string _falseExpression;

        /// <summary>
        /// Initializes a new instance of the
        /// NServiceKit.OrmLite.Firebird.FirebirdSqlExpressionVisitor&lt;T&gt; class.
        /// </summary>
	    public FirebirdSqlExpressionVisitor()
        {
            _trueExpression = string.Format("({0}={1})", GetQuotedTrueValue(), GetQuotedTrueValue());
            _falseExpression = string.Format("({0}={1})", GetQuotedTrueValue(), GetQuotedFalseValue());
        } 

        /// <summary>Visit binary.</summary>
        /// <param name="b">The BinaryExpression to process.</param>
        /// <returns>An object.</returns>
	    protected override object VisitBinary(BinaryExpression b)
        {
            object left, right;
            var operand = BindOperant(b.NodeType);   //sep= " " ??
            if (operand == "AND" || operand == "OR")
            {
                var m = b.Left as MemberExpression;
                if (m != null && m.Expression != null
                    && m.Expression.NodeType == ExpressionType.Parameter)
                    left = new PartialSqlString(string.Format("{0}={1}", VisitMemberAccess(m), GetQuotedTrueValue()));
                else
                    left = Visit(b.Left);

                m = b.Right as MemberExpression;
                if (m != null && m.Expression != null
                    && m.Expression.NodeType == ExpressionType.Parameter)
                    right = new PartialSqlString(string.Format("{0}={1}", VisitMemberAccess(m), GetQuotedTrueValue()));
                else
                    right = Visit(b.Right);

                if (left as PartialSqlString == null && right as PartialSqlString == null)
                {
                    var result = Expression.Lambda(b).Compile().DynamicInvoke();
                    return new PartialSqlString(OrmLiteConfig.DialectProvider.GetQuotedValue(result, result.GetType()));
                }

                if (left as PartialSqlString == null)
                    left = ((bool)left) ? GetTrueExpression() : GetFalseExpression();
                if (right as PartialSqlString == null)
                    right = ((bool)right) ? GetTrueExpression() : GetFalseExpression();
            }
            else
            {
                left = Visit(b.Left);
                right = Visit(b.Right);

                if (left as EnumMemberAccess != null)
                {
                    var enumType = ((EnumMemberAccess)left).EnumType;

                    //enum value was returned by Visit(b.Right)
                    long numvericVal;
                    if (Int64.TryParse(right.ToString(), out numvericVal))
                        right = OrmLiteConfig.DialectProvider.GetQuotedValue(Enum.ToObject(enumType, numvericVal).ToString(),
                                                                     typeof(string));
                    else
                        right = OrmLiteConfig.DialectProvider.GetQuotedValue(right, right.GetType());
                }
                else if (right as EnumMemberAccess != null)
                {
                    var enumType = ((EnumMemberAccess)right).EnumType;

                    //enum value was returned by Visit(b.Left)
                    long numvericVal;
                    if (Int64.TryParse(left.ToString(), out numvericVal))
                        left = OrmLiteConfig.DialectProvider.GetQuotedValue(Enum.ToObject(enumType, numvericVal).ToString(),
                                                                     typeof(string));
                    else
                        left = OrmLiteConfig.DialectProvider.GetQuotedValue(left, left.GetType());
                }
                else if (left as PartialSqlString == null && right as PartialSqlString == null)
                {
                    var result = Expression.Lambda(b).Compile().DynamicInvoke();
                    return result;
                }
                else if (left as PartialSqlString == null)
                    left = OrmLiteConfig.DialectProvider.GetQuotedValue(left, left != null ? left.GetType() : null);
                else if (right as PartialSqlString == null)
                    right = OrmLiteConfig.DialectProvider.GetQuotedValue(right, right != null ? right.GetType() : null);

            }

            if (operand == "=" && right.ToString() == "null") operand = "is";
            else if (operand == "<>" && right.ToString() == "null") operand = "is not";
            else if (operand == "=" || operand == "<>")
            {
                if (IsTrueExpression(right)) right = GetQuotedTrueValue();
                else if (IsFalseExpression(right)) right = GetQuotedFalseValue();

                if (IsTrueExpression(left)) left = GetQuotedTrueValue();
                else if (IsFalseExpression(left)) left = GetQuotedFalseValue();

            }

            switch (operand)
            {
                case "MOD":
                case "COALESCE":
                    return new PartialSqlString(string.Format("{0}({1},{2})", operand, left, right));
                default:
                    return new PartialSqlString("(" + left + Sep + operand + Sep + right + ")");
            }
        }

        /// <summary>Visit constant.</summary>
        /// <param name="c">The ConstantExpression to process.</param>
        /// <returns>An object.</returns>
        protected override object VisitConstant(ConstantExpression c)
        {
            if (c.Value == null)
                return new PartialSqlString("null");

            if (c.Value is bool)
            {
                object o = OrmLiteConfig.DialectProvider.GetQuotedValue(c.Value, c.Value.GetType());
                return new PartialSqlString(string.Format("({0}={1})", GetQuotedTrueValue(), o));
            }

            return c.Value;
        }

        /// <summary>Visit column access method.</summary>
        /// <param name="m">The MethodCallExpression to process.</param>
        /// <returns>An object.</returns>
        protected override object VisitColumnAccessMethod(MethodCallExpression m)
        {
            List<Object> args = this.VisitExpressionList(m.Arguments);
            var quotedColName = Visit(m.Object);
            var statement = "";

            switch (m.Method.Name)
            {
                case "Trim":
                    statement = string.Format("trim({0})", quotedColName);
                    break;
                case "LTrim":
                    statement = string.Format("trim(leading from {0})", quotedColName);
                    break;
                case "RTrim":
                    statement = string.Format("trim(trailing from {0})", quotedColName);
                    break;
                default:
                    return base.VisitColumnAccessMethod(m);
            }
            return new PartialSqlString(statement);
        }

        /// <summary>Query if 'exp' is true expression.</summary>
        /// <param name="exp">The exponent.</param>
        /// <returns>true if true expression, false if not.</returns>
        private bool IsTrueExpression(object exp)
        {
            return (exp.ToString() == _trueExpression);
        }

        /// <summary>Query if 'exp' is false expression.</summary>
        /// <param name="exp">The exponent.</param>
        /// <returns>true if false expression, false if not.</returns>
	    private bool IsFalseExpression(object exp)
        {
            return (exp.ToString() == _falseExpression);
        }

        /// <summary>Gets the limit expression.</summary>
        /// <exception cref="ArgumentException">Thrown when one or more arguments have unsupported or
        /// illegal values.</exception>
        /// <value>The limit expression.</value>
	    public override string LimitExpression{
			get{
				if(!Skip.HasValue) return "";
				int fromRow= Skip.Value+1;
				if(fromRow<=0)
					throw new ArgumentException(
						string.Format("Skip value:'{0}' must be>=0",Skip.Value ));
				string toRow;
				if(Rows.HasValue){
					if( Rows.Value<0) {
						throw new ArgumentException(
							string.Format("Rows value:'{0}' must be>=0", Rows.Value));
					}
					toRow= string.Format("TO {0}", fromRow+Rows.Value-1 );
				}
				else{
					toRow=string.Empty;
				}
				return string.Format("ROWS {0} {1}", fromRow, toRow);                   
			}
		}
				
	}
}
