```

public class Dog
{
    /// <summary>
    /// Tên của con chó.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Tuổi của con chó (tính theo năm).
    /// </summary>
    public int Age { get; set; }


    /// <summary>
    /// Cho chó sủa theo số lần chỉ định.
    /// </summary>
    /// <param name="time">Số lần chó sẽ sủa.</param>
    public void Bark(int time)
    {
        for (int i = 0; i < time; i++)
        {
            Console.WriteLine($"{Name} sủa: Gâu gâu!");
        }
    }

    /// <summary>
    /// Tăng tuổi con chó lên 1 năm.
    /// </summary>
    public void CelebrateBirthday()
    {
        Age++;
        Console.WriteLine($"{Name} vừa mừng sinh nhật! Bây giờ {Name} đã {Age} tuổi.");
    }
}

```
<img width="706" height="647" alt="image" src="https://github.com/user-attachments/assets/c1907e1e-7cfa-4e0b-bf0a-f76de0539bda" />

