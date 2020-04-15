// Copyright (c) MicroElements. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MicroElements.Bootstrap
{
    /// <summary>
    /// ���������� �� ���������.
    /// </summary>
    public class StartupInfo
    {
        /// <summary>
        /// ������ ����������.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// ������� ����������.
        /// </summary>
        public string CurrentDirectory { get; set; }

        /// <summary>Gets the pathname of the base directory that the assembly resolver uses to probe for assemblies.</summary>
        /// <returns>the pathname of the base directory that the assembly resolver uses to probe for assemblies.</returns>
        public string BaseDirectory { get; set; }

        /// <summary>
        /// ���� � ������������ ���������.
        /// </summary>
        public string StartupApp { get; set; }
    }
}
