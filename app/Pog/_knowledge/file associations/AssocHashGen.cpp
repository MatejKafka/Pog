/*
 * AssocHashGen - now you can set default browser in one click, just like on XP
 *
 * This is a crappy proof of concept tool to generate User Association hashes
 * in Windows 10 and 8.1.
 *
 * This should be all that's needed to solve the association problems that
 * Mozilla and Google have been complaining about. If microsoft still claims
 * this is a "security" measure and not just anti-competitive nonsense, it
 * defeats that "security" too.
 *
 * http://www.theverge.com/2015/7/30/9076445/mozilla-microsoft-windows-10-browser-default-apps-complaint
 * http://www.theverge.com/2015/10/18/9563927/microsoft-windows-10-default-apps-browser-prompt
 *
 * Technical details:
 * User choice hashes are pretty much just
 * MD5((protocol + sid + progid + exepath).toLower()) passed through some
 * custom scrambling functions. Dead simple. Not sure why no one else beat me
 * to it yet.
 *
 * Using Qt because I had it around and they have MD5 built in as well as
 * nice string manipulation functions.
 *
 * Sample Usage:
 * For URLs of http and https, provide SID to calculate this for any user on
 * any machine:
 * AssocHashGen -s "<SID>" -p "c:\program files (x86)\mozilla firefox\firefox.exe" http FirefoxURL
 * For all others (file extensions, non-http(s) URLs, etc) do NOT provide exe path:
 * AssocHashGen -s "<SID>" .htm ChromeHTML
 *
 * Tested on Windows 10 Enterprise and 8.1 Professional.
 * Algorithm has remained the same.
 *
 * WARNING:
 * This code lacks basic things like error handling, memory management and
 * buffer length checks. This is done "for clarity" but also "because lazy".
 * Do _not_ use it directly except for testing.
 */

#include <QCoreApplication>

#include <QCommandLineParser>
#include <iostream>
#include <Windows.h>
#include <sddl.h>
#include <vector>
#include <QDebug>
#include <QCryptographicHash>

void CS64_WordSwap(const unsigned int *data, unsigned int dataLength,
                   const unsigned int *md5Start, unsigned int *output)
{
    unsigned int v5; // er10@1
    const unsigned int *v6; // rdi@1
    unsigned int v7; // er11@3
    unsigned int v8; // ebx@3
    int v9; // er9@3
    int v10; // er11@3
    int v11; // ebx@3
    unsigned int v12; // er8@3
    __int64 v13; // rsi@3
    unsigned int v14; // er8@4
    unsigned int v15; // edx@4
    int v16; // er8@4
    int v17; // er9@4
    unsigned int v18; // edx@4
    char result; // al@6
    unsigned int v20; // er11@9
    unsigned int v21; // er8@9
    int v22; // er9@9

    v5 = dataLength;
    v6 = data;
    if (dataLength < 2 || dataLength & 1)
    {
        result = 0;
    }
    else
    {
        v7 = *md5Start | 1;
        v8 = md5Start[1] | 1;
        v9 = 0;
        v10 = v7 + 0x69FB0000;
        v11 = v8 + 0x13DB0000;
        v12 = 0;
        v13 = ((v5 - 2) >> 1) + 1;
        do
        {
            v14 = *v6 + v12;
            v6 += 2;
            v5 -= 2;
            v15 = 0x79F8A395 * (v14 * v10 - 0x10FA9605 * (v14 >> 16))
                + 0x689B6B9F * ((v14 * v10 - 0x10FA9605 * (v14 >> 16)) >> 16);
            v16 = 0xEA970001 * v15 - 0x3C101569 * (v15 >> 16);
            v17 = v16 + v9;
            v18 = (*(v6 - 1) + v16) * v11 - 0x3CE8EC25 * ((*(v6 - 1) + v16) >> 16);
            v12 = 0x1EC90001 * (0x59C3AF2D * v18 - 0x2232E0F1 * (v18 >> 16))
                + 0x35BD1EC9 * ((0x59C3AF2D * v18 - 0x2232E0F1 * (v18 >> 16)) >> 16);
            v9 = v12 + v17;
            --v13;
        } while (v13);
        if (v5 == 1)
        {
            v20 = (*v6 + v12) * v10 - 0x10FA9605 * ((*v6 + v12) >> 16);
            v21 = 0xEA970001 * (0x79F8A395 * v20 + 0x689B6B9F * (v20 >> 16))
                - 0x3C101569 * ((0x79F8A395 * v20 + 0x689B6B9F * (v20 >> 16)) >> 16);
            v22 = v21 + v9;
            v12 = 0x1EC90001
                * (0x59C3AF2D * (v21 * v11 - 0x3CE8EC25 * (v21 >> 16))
                    - 0x2232E0F1 * ((v21 * v11 - 0x3CE8EC25 * (v21 >> 16)) >> 16))
                + 901586633
                * ((0x59C3AF2D * (v21 * v11 - 0x3CE8EC25 * (v21 >> 16))
                    - 0x2232E0F1 * ((v21 * v11 - 0x3CE8EC25 * (v21 >> 16)) >> 16)) >> 16);
            v9 = v12 + v22;
        }
        result = 1;
        *output = v12;
        output[1] = v9;
    }
}

void CS64_Reversible(const unsigned int *data, unsigned int dataLength,
                     const unsigned int *md5, unsigned int *output)
{
    unsigned int v5; // er10@1
    const unsigned int *v6; // rdi@1
    unsigned int v7; // ebx@3
    int v8; // er11@3
    unsigned int v9; // er9@3
    int v10; // ebx@3
    unsigned int v11; // er8@3
    __int64 v12; // rsi@3
    unsigned int v13; // er8@4
    unsigned int v14; // er8@4
    unsigned int v15; // edx@4
    int v16; // er11@4
    unsigned int v17; // edx@4
    unsigned int v18; // edx@4
    char result; // al@6
    unsigned int v20; // edx@9
    unsigned int v21; // edx@9
    int v22; // er11@9
    unsigned int v23; // edx@9
    unsigned int v24; // eax@9

    v5 = dataLength;
    v6 = data;
    if (dataLength < 2 || dataLength & 1)
    {
        result = 0;
    }
    else
    {
        v7 = *md5;
        v8 = 0;
        v9 = md5[1] | 1;
        v10 = v7 | 1;
        v11 = 0;
        v12 = ((v5 - 2) >> 1) + 1;
        do
        {
            v5 -= 2;
            v13 = v10 * (*v6 + v11);
            v6 += 2;
            v14 = 0x5B9F0000 * (0xB1110000 * v13 - 0x30674EEF * (v13 >> 16))
                - 0x78F7A461
                * ((0xB1110000 * v13 - 0x30674EEF * (v13 >> 16)) >> 16);
            v15 = 0x1D830000 * (0x12CEB96D * (v14 >> 16) - 0x46930000 * v14)
                + 0x257E1D83
                * ((0x12CEB96D * (v14 >> 16) - 0x46930000 * v14) >> 16);
            v16 = v15 + v8;
            v17 = 0x16F50000 * v9 * (*(v6 - 1) + v15) - 0x5D8BE90B
                * (v9 * (*(v6 - 1) + v15) >> 16);
            v18 = 0x2B890000 * (0x96FF0000 * v17 - 0x2C7C6901 * (v17 >> 16))
                + 0x7C932B89
                * ((0x96FF0000 * v17 - 0x2C7C6901 * (v17 >> 16)) >> 16);
            v11 = 0x9F690000 * v18 - 0x405B6097 * (v18 >> 16);
            v8 = v11 + v16;
            --v12;
        } while (v12);
        if (v5 == 1)
        {
            v20 = 0xB1110000 * v10 * (v11 + *v6) - 0x30674EEF
                * (v10 * (v11 + *v6) >> 16);
            v21 = 0x1D830000
                * (0x12CEB96D * ((0x5B9F0000 * v20 - 0x78F7A461 * (v20 >> 16)) >> 16)
                    - 0x46930000 * (0x5B9F0000 * v20 - 0x78F7A461 * (v20 >> 16)))
                + 0x257E1D83
                * ((0x12CEB96D * ((0x5B9F0000 * v20 - 0x78F7A461 * (v20 >> 16)) >> 16)
                    - 0x46930000 * (0x5B9F0000 * v20 - 0x78F7A461 * (v20 >> 16))) >> 16);
            v22 = v21 + v8;
            v23 = 0x16F50000 * v9 * v21 - 0x5D8BE90B * (v9 * v21 >> 16);
            v24 = (0x96FF0000 * v23 - 0x2C7C6901 * (v23 >> 16)) >> 16;
            v11 = 0x9F690000 * (0x2B890000 * (0x96FF0000 * v23 - 0x2C7C6901 * (v23 >> 16)) + 0x7C932B89 * v24)
                - 0x405B6097 * ((0x2B890000 * (0x96FF0000 * v23 - 0x2C7C6901 * (v23 >> 16)) + 0x7C932B89 * v24) >> 16);
            v8 = v11 + v22;
        }
        result = 1;
        *output = v11;
        output[1] = v8;
    }
}

QString getSid()
{
    // shit code with no error handling. if we can't get the sid, just tank, lazy PoC
    HANDLE hToken = NULL;
    OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken);
    DWORD dwBufferSize = 0;
    GetTokenInformation(hToken, TokenUser, NULL, 0, &dwBufferSize);
    std::vector<BYTE> buffer;
    buffer.resize(dwBufferSize);
    PTOKEN_USER pTokenUser = reinterpret_cast<PTOKEN_USER>(&buffer[0]);

    GetTokenInformation(
        hToken,
        TokenUser,
        pTokenUser,
        dwBufferSize,
        &dwBufferSize);

    LPWSTR output;
    ConvertSidToStringSidW(pTokenUser->User.Sid, &output);

    CloseHandle(hToken);
    hToken = NULL;

    return QString::fromWCharArray(output);
}


QString genHash(QString protocol, QString exepath, QString sid, QString progid)
{
    // start out zero'd because towchararray doesn't append 0s and laziness
    wchar_t* data = (wchar_t*)calloc(1024, 1);
    QString((protocol + sid + progid + exepath).toLower()).toWCharArray(data);
    QCryptographicHash hash(QCryptographicHash::Md5);
    int dataLength = wcslen(data)*2+2;
    hash.addData((char *)data, dataLength);
    int v6 = dataLength >> 2;
    if ((dataLength >> 2) & 1)
        --v6;

    // result of the aforementioned md5 operation
    unsigned int* md5 = (unsigned int*)hash.result().data();
    unsigned int out[2];
    unsigned int out2[2];
    CS64_WordSwap((unsigned int *)data, v6, md5, out);
    CS64_Reversible((unsigned int *)data, v6, md5, out2);

    unsigned int finalResult[2] = { out[0] ^ out2[0], out[1] ^ out2[1] };
    return QByteArray((char*)finalResult, sizeof(finalResult)).toBase64();
}

int main(int argc, char *argv[]) {
    if (argc != 3 && argc != 4) {
        return 1;
    }

    const char* protocol = argv[1];
    const char* progid = argv[2];
    // only needed for http/https protocol
    const char* exe_path = argc == 4 ? argv[3] : nullptr;

    genHash(protocol, , , progid);

    if (parser.positionalArguments().size() < 2)
        parser.showHelp();
    QTextStream ts( stdout );
    ts << genHash(parser.positionalArguments().first(), parser.value("p"),
                  parser.value("s"), parser.positionalArguments().last())
       << endl;
}