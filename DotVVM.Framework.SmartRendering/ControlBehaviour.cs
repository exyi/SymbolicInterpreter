using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DotVVM.Framework.SmartRendering
{
    public class ControlBehaviour
    {
        public ParameterExpression[] Variables { get; set; }
        public Expression Initialize { get; set; }
        public Expression[] LifecycleEvents { get; set; }
        public RenderedOutput[] Renders { get; set; }
    }
}
