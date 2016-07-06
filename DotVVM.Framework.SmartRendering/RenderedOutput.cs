using DotVVM.Framework.Binding;
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
		public abstract string GetDebugText();
    }
    public class RenderedText: RenderedOutput
    {
        public RenderedText(string text)
        {
            this.Text = text;
        }

        public string Text { get; }

		public override string GetDebugText() => Text;
	}
    public class RenderControls: RenderedOutput
    {
        public ImmutableList<ResolvedControl> Controls { get; }

        public RenderControls(ImmutableList<ResolvedControl> controls)
        {
            Controls = controls;
        }

		public override string GetDebugText() => "{{" + Controls.Count + " controls}}";
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
    //public class RenderBindingValue : Renderedoutput
    //{
    //    public resolvedbinding binding { get; }
    //    public resolvedcontrol contextcontrol { get; }
    //    public dotvvmproperty contextproperty { get; }
    //    public renderbindingvalue(resolvedbinding binding, resolvedcontrol contextcontrol, dotvvmproperty contextproperty)
    //    {
    //        binding = binding;
    //        contextcontrol = contextcontrol;
    //        contextproperty = contextproperty;
    //    }
    //}
    public class RenderExpressionValue: RenderedOutput
    {
        public Expression Expression { get; }
        public RenderExpressionValue(Expression expr)
        {
            Expression = expr;
        }

		public override string GetDebugText() => "{{" + Expression.ToString() + "}}";
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
