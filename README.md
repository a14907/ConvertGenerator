利用dotnet core的代码生成的特性，自动生成类型转换的代码。类似于AutoMaper。

生成通过比较属性名字（不区分大小写）

属性支持简单类型，类，List，Dictionary(key最好是string类型)

在需要转换的类上标记特性：ConvertFrom、ConvertTo

```
    [ConvertTo(typeof(B))]
    [ConvertFrom(typeof(B))]
    public partial class A
    {
        public int Age { get; set; }
        public Dictionary<string, A2> Dic { get; set; }
    }
    public partial class A2
    {
        public int MyProperty { get; set; }
    }

    public class B
    {
        public int Age { get; set; }
        public Dictionary<string,B2> Dic { get; set; }
    }

    public class B2
    {
        public int MyProperty { get; set; }
    }
```

上面的例子生成:

```
	public partial class A
	{
		public static A ConvertFrom(ConverterTest.B item)
		{
			if(item != null) 
			{
				var r = new ConverterTest.A();
				r.Age = item.Age;
				if(item.Dic != null) r.Dic = item.Dic.ToDictionary(a => a.Key, b => 
				{
					if(b.Value != null) 
					{
						var c2 = new ConverterTest.A2();
						c2.MyProperty = b.Value.MyProperty;
						return c2;
					} else return null;
				});
				return r;
			} else return null;

		}
		public static List<A> ConvertListFrom(IList<ConverterTest.B> items) => items.Select(p => A.ConvertFrom(p)).ToList();

	}

	public partial class A
	{
		public ConverterTest.B ConvertToB()
		{
			if(this != null) 
			{
				var r = new ConverterTest.B();
				r.Age = this.Age;
				if(this.Dic != null) r.Dic = this.Dic.ToDictionary(a => a.Key, b =>  
				{
					if(b.Value != null) 
					{
						var c2 = new ConverterTest.B2();
						c2.MyProperty = b.Value.MyProperty;
						return c2;
					} else return null;
				});
				return r;
			} else return null;

		}
		public static List<ConverterTest.B> ConvertListTo(IList<ConverterTest.A> items) => items.Select(p => p.ConvertToB()).ToList();

	}
```

安装：

直接在nuget上搜索：ConvertGenerator

地址：https://www.nuget.org/packages/ConvertGenerator

或者：

```
dotnet add package ConvertGenerator --version 1.0.1
```