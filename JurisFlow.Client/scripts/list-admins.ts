import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

async function listAdmins() {
  try {
    console.log('Admin hesaplari sorgulaniyor...\n');

    const admins = await prisma.user.findMany({
      where: {
        role: 'Admin'
      },
      select: {
        email: true,
        name: true,
        role: true,
        createdAt: true
      },
      orderBy: {
        createdAt: 'desc'
      }
    });

    if (admins.length === 0) {
      console.log('Sistemde admin kullanicisi bulunamadi.');
      return;
    }

    console.log(`Toplam ${admins.length} admin kullanicisi bulundu:\n`);

    admins.forEach((admin, index) => {
      console.log(`${index + 1}. ${admin.name || 'Isimsiz'}`);
      console.log(`   Email: ${admin.email}`);
      console.log(`   Rol: ${admin.role}`);
      console.log(`   Olusturulma: ${admin.createdAt ? new Date(admin.createdAt).toLocaleString('tr-TR') : 'Bilinmiyor'}`);
      console.log('');
    });

    console.log('Bilinen sifreler repoda tutulmaz; gerekiyorsa guvenli parola yoneticisinden alin.');
  } catch (error) {
    console.error('Hata:', error);
    process.exit(1);
  } finally {
    await prisma.$disconnect();
  }
}

listAdmins();
