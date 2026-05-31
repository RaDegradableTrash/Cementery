import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# We need to move GetTotalDeformation AFTER GetSandDeformation
# It currently looks like:
#             float GetTotalDeformation(float3 posWS)
#             {
#                 return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);
#             }
#
#             float GetSandDeformation(float3 posWS)
#             {
#                 float totalDisp = 0.0;
#                 ...
#                 return totalDisp;
#             }

# Find this pattern and swap them. 
# We can do this safely by first removing GetTotalDeformation, then appending it after GetSandDeformation.

def_str = """
            float GetTotalDeformation(float3 posWS)
            {
                return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);
            }
"""

content = content.replace(def_str, "")

# Now find the end of GetSandDeformation
# We can search for the end of GetSandDeformation which is:
#                 }
#                 return totalDisp;
#             }

content = content.replace(
"""                }
                return totalDisp;
            }""", 
"""                }
                return totalDisp;
            }
""" + def_str
)

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

