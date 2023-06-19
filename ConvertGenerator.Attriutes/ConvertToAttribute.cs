using System;

namespace ConvertGenerator.Attriutes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ConvertToAttribute : Attribute
    {
        private readonly Type _type;

        public ConvertToAttribute(Type type)
        {
            _type = type;
        }
    }
}
