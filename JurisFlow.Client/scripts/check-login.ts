import { PrismaClient } from '@prisma/client';
import bcrypt from 'bcryptjs';

const prisma = new PrismaClient();

type TestAccount = {
  email: string;
  password: string;
};

const rawTestAccounts = process.env.LOGIN_CHECK_ACCOUNTS_JSON;
const testAccounts: TestAccount[] = rawTestAccounts ? JSON.parse(rawTestAccounts) : [];

async function checkLogin() {
  try {
    console.log('Login kontrolu yapiliyor...\n');

    if (testAccounts.length === 0) {
      console.log('LOGIN_CHECK_ACCOUNTS_JSON tanimli degil; sifre kontrolu atlandi.');
    }

    for (const account of testAccounts) {
      console.log(`\nKontrol ediliyor: ${account.email}`);

      const user = await prisma.user.findUnique({
        where: { email: account.email }
      });

      if (!user) {
        console.log('   Kullanici bulunamadi.');
        continue;
      }

      console.log('   Kullanici bulundu');
      console.log(`   Isim: ${user.name}`);
      console.log(`   Rol: ${user.role}`);
      console.log(`   Olusturulma: ${user.createdAt ? new Date(user.createdAt).toLocaleString('tr-TR') : 'Bilinmiyor'}`);

      if (user.passwordHash) {
        const isValid = await bcrypt.compare(account.password, user.passwordHash);
        console.log(isValid ? '   Sifre dogru.' : '   Sifre yanlis.');
      } else {
        console.log('   Sifre hash bulunamadi.');
      }
    }

    console.log('\nTum admin kullanicilari listeleniyor:\n');

    const allAdmins = await prisma.user.findMany({
      where: { role: 'Admin' },
      select: {
        email: true,
        name: true,
        role: true,
        passwordHash: true,
        createdAt: true
      }
    });

    console.log(`Toplam ${allAdmins.length} admin kullanicisi:\n`);
    allAdmins.forEach((admin, index) => {
      console.log(`${index + 1}. ${admin.name || 'Isimsiz'}`);
      console.log(`   Email: ${admin.email}`);
      console.log(`   Sifre Hash: ${admin.passwordHash ? 'Var' : 'Yok'}`);
      console.log(`   Olusturulma: ${admin.createdAt ? new Date(admin.createdAt).toLocaleString('tr-TR') : 'Bilinmiyor'}`);
      console.log('');
    });
  } catch (error) {
    console.error('Hata:', error);
    process.exit(1);
  } finally {
    await prisma.$disconnect();
  }
}

checkLogin();
