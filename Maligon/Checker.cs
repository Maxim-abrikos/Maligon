using System;
using System.Collections.Generic;
using System.Text;

namespace Maligon
{
    public class Checker
    {
        //FBX, Collada, GLTF, OBJ, 3DS, DXF
        private List<string> Formats = new List<string>() {"obj", "gltf", "FBX" };

        public (bool, string) CheckFormat(string[] Texts)
        {
            if (Texts.Length > 1)
            {
                return (false, "Программа не поддерживает работу с массивами объектов");
            }
            else
            {
                if (Formats.Contains(Texts[0].Split(".").Last()))
                    return (true, Texts[0]);
                else
                    return (false, "Выбранный формат не поддерживается");
            }
        }
    }
}
