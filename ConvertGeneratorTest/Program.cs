
using ConvertGenerator.Attriutes;
using System.Text.Json;
using System.Collections.Generic;

namespace ConverterTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

        }
    }

    public partial class C1
    {
        internal partial struct C2
        {
            [ConvertFrom(typeof(D1.D2))]
            public partial class C3
            {
                public int P1 { get; set; }
            }
        }
    }

    public class D1
    {
        public class D2
        {
            public int P1 { get; set; }

        }
    }

    [ConvertFrom(typeof(G))]
    [ConvertTo(typeof(G))]
    public partial class F
    {
        public int P1 { get; set; }
    }
    public class G
    {
        public int P1 { get; set; }
    }

}


[ConvertFrom(typeof(B))]
public partial class A
{
    public int MyProperty { get; set; }
}

public class B
{
    public int MyProperty { get; set; }
}
