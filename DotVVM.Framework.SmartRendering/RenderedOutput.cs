using DotVVM.Framework.Compilation.ControlTree.Resolved;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public abstract class RenderedOutput
    {
    }
    public class RenderedText: RenderedOutput
    {
        public RenderedText(string text)
        {
            this.Text = text;
        }

        public string Text { get; }
    }
    public class RenderControls: RenderedOutput
    {
        public ImmutableList<ResolvedControl> Controls { get; }

        public RenderControls(ImmutableList<ResolvedControl> controls)
        {
            Controls = controls;
        }
    }

    public class RenderControlsExpression : Expression
    {
        public ImmutableList<ResolvedControl> Controls { get; }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public RenderControlsExpression(ImmutableList<ResolvedControl> controls)
        {
            Controls = controls;
        }
    }
    //public class RenderBindingValue: RenderedOutput
    //{
    //    public ResolvedBinding Binding { get; }
    //    public ImmutableList<LambdaExpression> PostProcess { get; }

    //    public RenderBindingValue(ResolvedBinding binding, ImmutableList<LambdaExpression> postProcess = null)
    //    {
    //        Binding = binding;
    //        PostProcess = postProcess ?? ImmutableList<LambdaExpression>.Empty;
    //    }

    //    public RenderBindingValue AddPostProcess(LambdaExpression lamdaExpression) => new RenderBindingValue(Binding, PostProcess.Add(lamdaExpression));
    //}
    public class RenderExpressionValue: RenderedOutput
    {
        public Expression Expression { get; }
        public RenderExpressionValue(Expression expr)
        {
            Expression = expr;
        }
    }
    //public class EvaluateExpression: RenderedOutput
    //{
    //    public Expression Expression { get; }
    //    public bool DependsOnWriter { get; }

    //    public EvaluateExpression(Expression expr, bool dependsOnWriter)
    //    {
    //        Expression = expr;
    //        DependsOnWriter = dependsOnWriter;
    //    }
    //}
}
