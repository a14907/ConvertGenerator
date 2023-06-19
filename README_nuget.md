利用dotnet core的代码生成的特性，自动生成类型转换的代码。类似于AutoMaper。

生成通过比较属性名字（不区分大小写）

属性支持简单类型，类，List，Dictionary(key最好是string类型)

在需要转换的类上标记特性：ConvertFrom、ConvertTo