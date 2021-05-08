using System;

namespace ConsoleApplication1
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var person = new Person(){Name="Patrick"};
            Console.WriteLine(person.Name);
            HelloWorldGenerated.HelloWorld.SayHello();
        }
    }
}