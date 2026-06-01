import re

with open('Assets/Scripts/SnowParticleSystem.cs', 'r') as f:
    content = f.read()

# Remove snowPastePS declaration
content = re.sub(r'\s*private ParticleSystem snowPastePS;', '', content)

# Remove the SubEmitter creation logic inside ConfigureParticleSystemProgrammatically
content = re.sub(
    r'\s*// 7\. 结算为扁平的膏状独立3D物体 \(Sub Emitter\).*?snowPastePS = transform\.Find\("SnowPasteEmitter"\)\.GetComponent<ParticleSystem>\(\);\s*\}\s*\}',
    '\n    }',
    content,
    flags=re.DOTALL
)

# Remove manual emit inside OnParticleCollision
content = re.sub(
    r'\s*// 手动在碰撞点生成一个 3D 膏状雪堆！绝对 100% 触发！\s*if \(snowPastePS != null\)\s*\{\s*var emitParams = new ParticleSystem\.EmitParams\(\);\s*emitParams\.position = pos;\s*emitParams\.rotation3D = new Vector3\(0, Random\.Range\(0, 360f\), 0\); // 随机旋转增加自然感\s*snowPastePS\.Emit\(emitParams, 1\);\s*\}',
    '',
    content,
    flags=re.DOTALL
)

with open('Assets/Scripts/SnowParticleSystem.cs', 'w') as f:
    f.write(content)

