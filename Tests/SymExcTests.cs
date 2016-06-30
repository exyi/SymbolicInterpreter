using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DotVVM.Framework.SmartRendering;
using System.Reflection;
using DotVVM.Framework.Controls;
using System.Linq.Expressions;
using System.Linq;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Parser;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Binding;
using System.Collections.Generic;
using SymbolicInterpreter;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Controls.Infrastructure;

namespace Tests
{
    class SimpleDotvvmControl: HtmlGenericControl
    {
        public string CssClass
        {
            get { return (string)GetValue(CssClassProperty); }
            set { SetValue(CssClassProperty, value); }
        }
        public static readonly DotvvmProperty CssClassProperty
            = DotvvmProperty.Register<string, SimpleDotvvmControl>(c => c.CssClass, null);

        public SimpleDotvvmControl(): base("div")
        {

        }


        protected override void AddAttributesToRender(IHtmlWriter writer, IDotvvmRequestContext context)
        {
            writer.AddAttribute("class", CssClass);
            base.AddAttributesToRender(writer, context);
        }

        protected override void RenderBeginTag(IHtmlWriter writer, IDotvvmRequestContext context)
        {
            //writer.RenderBeginTag("div");
            base.RenderBeginTag(writer, context);
        }

        protected override void RenderEndTag(IHtmlWriter writer, IDotvvmRequestContext context)
        {
            //writer.RenderEndTag();
            base.RenderEndTag(writer, context);
        }
    }

    [TestClass]
    public class SymExcTests
    {
        MethodInfo htmlGenericControlRender = typeof(HtmlGenericControl).GetMethod("AddHtmlAttributes", BindingFlags.NonPublic | BindingFlags.Instance);

        private DotvvmConfiguration configuration;
        private IControlTreeResolver controlTreeResolver;
        private SymbolicExecutor sexec;
        private ControlAnalyzer controlAnalyzer;

        [TestInitialize()]
        public void TestInit()
        {
            ExpressionComparer.Instance.CacheHashValues();

            configuration = DotvvmConfiguration.CreateDefault();
            configuration.Markup.AddAssembly(typeof(SymExcTests).Assembly.FullName);
            configuration.Markup.AddCodeControl("cc", typeof(SimpleDotvvmControl));
            controlTreeResolver = configuration.ServiceLocator.GetService<IControlTreeResolver>();
            sexec = new SymbolicExecutor();
            controlAnalyzer = new ControlAnalyzer(sexec);
            DotvvmSpecialExecutors.RegisterAll();
        }

        private ResolvedTreeRoot ParseSource(string markup, string fileName = "default.dothtml")
        {
            var tokenizer = new DothtmlTokenizer();
            tokenizer.Tokenize(new StringReader(markup));

            var parser = new DothtmlParser();
            var tree = parser.Parse(tokenizer.Tokens);

            return (ResolvedTreeRoot)controlTreeResolver.ResolveTree(tree, fileName);
        }
        private ExecutionState CallMethodHere(ExecutionState state, string name, SymbolicExecutor sexec = null)
        {
            if (sexec == null) sexec = this.sexec;
            return sexec.CallMethod(typeof(SymExcTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic), state, false);
        }

        [TestMethod]
        [Ignore]
        public void Test1()
        {
            var se = new SymbolicExecutor();
            var d = SymbolicExecutor.Disassembly(htmlGenericControlRender);
            var thisP = MyExpression.RootParameter(typeof(HtmlGenericControl), "this");
            var writerP = MyExpression.RootParameter(typeof(IHtmlWriter), "writer");
            var x = se.Execute(new ExecutionState(), d, new[] { thisP, writerP });
        }

        private ExecutionState ExecNode(string markup)
        {
            var tree = ParseSource(markup);
            var node = tree.Content.First(n => n.Metadata.Type != typeof(RawLiteral));
            return controlAnalyzer.ExecuteControlLifecycle(node);
        }

        [TestMethod]
        public void TestSimpleResolvedControl()
        {
            var tree = ParseSource(@"
<cc:SimpleDotvvmControl CssClass='{value: 'ahoj'}' data-attribute='{resource: '42'}' > neco uvnitr </cc:SimpleDotvvmControl>");
            var node = tree.Content.First(n => n.Metadata.Type == typeof(SimpleDotvvmControl));
            var state = controlAnalyzer.ExecuteControlLifecycle(node);

            var a = controlAnalyzer.AnalyzeControlBehaviour(state, node);
            
            var results = state.SideEffects.Where(s => s.Key == null).Select(k => k.Value).ToArray();
        }

        [TestMethod]
        public void TestHtmlGenericControl()
        {
            var tree = ParseSource(@"
<div class='class12' > neco uvnitr </div>");
            var node = tree.Content.First(n => n.Metadata.Type == typeof(HtmlGenericControl));
            var state = controlAnalyzer.ExecuteControlLifecycle(node);

            var a = controlAnalyzer.AnalyzeControlBehaviour(state, node);

            var results = state.SideEffects.Where(s => s.Key == null).Select(k => k.Value).ToArray();
        }

        [TestMethod]
        public void TestResolvedControl1()
        {
            var tree = ParseSource(@"
<div class='ahoj' />");
            var node = tree.Content.First(n => n.Metadata.Type == typeof(HtmlGenericControl));

            var state = controlAnalyzer.CreateExecutionState(node);
            state = CallMethodHere(state, "GetTagName");
            Assert.AreEqual(state.Stack.Single().Resolve(state).GetConstantValue(), "div");
        }

        private static string GetTagName(HtmlGenericControl control)
        {
            return control.TagName;
        }

        [TestMethod]
        public void TestResolvedcontrol_SideEffect()
        {
            var tree = ParseSource(@"
<div class='ahoj' />");
            var node = tree.Content.First(n => n.Metadata.Type == typeof(HtmlGenericControl));

            var state = controlAnalyzer.CreateExecutionState(node);
            state = state.WithStack(state.Stack.Concat(new[] { MyExpression.RootParameter(typeof(IHtmlWriter), "writer") }));
            state = CallMethodHere(state, "WriteTag");

            Assert.AreEqual(state.SideEffects.Count, 1);
            Assert.AreEqual(state.SideEffects.Single().Value.NodeType, ExpressionType.Call);
        }
        private static void WriteTag(HtmlGenericControl control, IHtmlWriter writer)
        {
            writer.WriteUnencodedText($"<{control.TagName}>");
        }

        class SimpleTestControl : DotvvmControl
        {
            public string RenderString
            {
                get { return (string)GetValue(RenderStringProperty); }
                set { SetValue(RenderStringProperty, value); }
            }
            public static readonly DotvvmProperty RenderStringProperty
                = DotvvmProperty.Register<string, SimpleTestControl>(c => c.RenderString, null);


        }

        [TestMethod]
        public void TestExceptionSideEffect()
        {
            var state = new ExecutionState()
                .WithStack(new[] { Expression.Constant("ERROR") });
            state = CallMethodHere(state, nameof(TryFinalyException));
            Assert.AreEqual(state.SideEffects.Single().Value.NodeType, ExpressionType.Throw);
        }

        private static void TryFinalyException(string error)
        {
            if (error != null)
                throw new Exception(error);
        }

        [TestMethod]
        public void Test_If_And_Exception()
        {
            var state = new ExecutionState()
                .WithStack(new[] { MyExpression.RootParameter(typeof(string), "error") });
            state = CallMethodHere(state, nameof(TryFinalyException));
            Assert.AreEqual(state.SideEffects.Single().Value.NodeType, ExpressionType.Throw);
        }

        private static int AbsoluteValue(int number)
        {
            if (number >= 0) return number;
            else return -number;
        }

        [TestMethod]
        public void Test_If()
        {
            var p = MyExpression.RootParameter(typeof(int), "input");
            var state = new ExecutionState()
                .WithStack(new[] { p });
            state = CallMethodHere(state, nameof(AbsoluteValue));
            Assert.AreEqual(state.SideEffects.Count, 0);
            var expression = state.Stack.Single().Resolve(state, fullResolve: true);
            Assert.AreEqual(expression.NodeType, ExpressionType.Conditional);
            Assert.AreEqual(((ConditionalExpression)expression).IfFalse, p);
            Assert.IsTrue(ExpressionComparer.Instance.Equals(expression, Expression.Condition(Expression.LessThan(p, Expression.Constant(0)), Expression.Negate(p), p)));
        }
        private static IEnumerable<int> YieldEnumerator()
        {
            for (int i = 10; ; i++)
            {
                yield return i;
            }
        }

        private static int ForeachReturnFirst()
        {
            foreach (var item in YieldEnumerator())
            {
                return item;
            }
            throw new Exception();
        }

        [TestMethod]
        public void Test_ForeachReturnFirst()
        {
            var state = new ExecutionState().WithStack(new Expression[0]);
            state = CallMethodHere(state, nameof(ForeachReturnFirst));
            //Assert.AreEqual(state.SideEffects.Count, 0);
            var expression = state.Stack.Single().Resolve(state, fullResolve: true);
            Assert.IsTrue(expression.IsConstant());
            Assert.AreEqual(expression.GetConstantValue(), 10);
        }

        private static IEnumerable<int> YieldEnumeratorParameter(int p)
        {
            for (int i = p; ; i++)
            {
                yield return i;
            }
        }

        private static int ForeachReturnFirstParameter(int p)
        {
            int i = 0;
            foreach (var item in YieldEnumeratorParameter(p)/*.Skip(1)*/)
            {
                if(i == 1) return item;
                i++;
            }
            throw new Exception();
        }

        [TestMethod]
        public void Test_ForeachReturnFirstParameter()
        {
            var p = MyExpression.RootParameter(typeof(int), "input");
            var state = new ExecutionState().WithStack(new[] { p });
            state = CallMethodHere(state, nameof(ForeachReturnFirstParameter));
            //Assert.AreEqual(state.SideEffects.Count, 0);
            var expression = state.Stack.Single().Resolve(state, fullResolve: true);
            Assert.IsTrue(ExpressionComparer.Instance.Equals(expression, Expression.AddChecked(Expression.Constant(1), p)));
        }

        private static int ForeachReturnFirstParameterWhereDivisibleBy(int divisibleBy)
        {
            foreach (var item in YieldEnumeratorParameter(5).Where(p => p % divisibleBy == 0))
            {
                return item;
            }
            throw new Exception();
        }

        [TestMethod]
        public void Test_ForeachWhere()
        {
            //var p = MyExpression.RootParameter(typeof(int), "input");
            var state = new ExecutionState().WithStack(new[] { Expression.Constant(2) });
            state = CallMethodHere(state, nameof(ForeachReturnFirstParameterWhereDivisibleBy));
            //Assert.ArEqual(state.SideEffects.Count, 0);
            var expression = state.Stack.Single().Resolve(state, fullResolve: true);
            Assert.IsTrue(expression.IsConstant());
            Assert.AreEqual(6, expression.GetConstantValue());
        }

        static void AddAttributesToWriter(IHtmlWriter writer, Dictionary<string, string> dictionary)
        {
            foreach (var attr in dictionary.Where(k => k.Key.StartsWith("a")))
            {
                writer.AddAttribute(attr.Key, attr.Value);
            }
        }

        static Dictionary<string, string> NewDictionary(string prefix)
        {
            var d = new Dictionary<string, string>();
            d["ahojkey"] = prefix + "ahojvalue";
            d["akkkkey"] = prefix + "akkkvalue";
            d["bubakey"] = prefix + "bubavalue";
            return d;
        }

        [TestMethod]
        public void Test_FoerachWhereOnDictionary()
        {
            var p1 = MyExpression.RootParameter(typeof(string), "inputValue1");

            var state = new ExecutionState();

            state = CallMethodHere(state.WithStack(new[] { p1 }), nameof(NewDictionary));
            var dict = state.Stack.Single();
            var writer = MyExpression.RootParameter(typeof(IHtmlWriter), "writer");
            state = CallMethodHere(state.WithStack(new[] { writer, dict }), nameof(AddAttributesToWriter));
        }

        [TestMethod]
        public void Test_RawHtmlGenericControl()
        {
            var state = ExecNode(@"<div></div>");
            var eff = new HashSet<Expression>(state.SideEffects.Where(k => k.Key == null).Select(k => k.Value), ExpressionComparer.Instance);
            var str = state.ToString();
        }

        [TestMethod]
        public void Test_SimpleAttributeHtmlGenericControl()
        {
            var state = ExecNode(@"<div class='class654'></div>");
            //var eff = new HashSet<Expression>(state.SideEffects.Where(k => k.Key == null).Select(k => k.Value), ExpressionComparer.Instance);
            var str = state.ToString();
        }

        [TestMethod]
        public void Test_BindingAttributeHtmlGenericControl()
        {
            var state = ExecNode(@"
@viewModel System.String
<div class='{value: _this}'></div>");
            //var eff = new HashSet<Expression>(state.SideEffects.Where(k => k.Key == null).Select(k => k.Value), ExpressionComparer.Instance);
            var str = state.ToString();
        }

    }

    //public static class EXTT
    //{
    //    public static IEnumerable<int> Skip(this IEnumerable<int> ahoj, int count)
    //    {
    //        return ahoj;
    //    }
    //}
}
