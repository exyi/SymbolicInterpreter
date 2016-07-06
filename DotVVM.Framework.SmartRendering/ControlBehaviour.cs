using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public class ControlBehaviour
    {
        public ImmutableArray<ParameterExpression> Variables { get; }
        public Expression Initialize { get; }
        public ImmutableArray<Expression> LifecycleEvents { get; }
        public ImmutableArray<RenderedOutput> Renders { get; }

        public ControlBehaviour(IEnumerable<ParameterExpression> variables,
            Expression initialize,
            IEnumerable<RenderedOutput> renders,
            IEnumerable<Expression> lifecycleEvents)
        {
            this.Variables = variables?.ToImmutableArray() ?? ImmutableArray<ParameterExpression>.Empty;
            this.Initialize = initialize;
            this.LifecycleEvents = lifecycleEvents?.ToImmutableArray() ?? ImmutableArray<Expression>.Empty;
            this.Renders = renders?.ToImmutableArray() ?? ImmutableArray<RenderedOutput>.Empty;
        }

		public override string ToString()
		{
			return string.Concat(Renders.Select(r => r.GetDebugText()));
		}
	}
}
