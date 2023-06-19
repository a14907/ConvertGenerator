using System;

namespace ConvertGenerator.Attriutes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ConvertFromAttribute : Attribute
    {
        private readonly Type _type;

        public ConvertFromAttribute(Type type)
        {
            _type = type;
        }
    }
}
