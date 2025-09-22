﻿using ScrapperWebAPI.Models.ProductDtos;
using ScrapperWebAPI.Models.Zara.Product;

namespace ScrapperWebAPI.Helpers.Mappers;
public static class RootMapper
{
    public static ProductToListDto MapToDto(Root root)
    {
        var product = root?.product;
        if (product == null)
            return null;

        var firstColor = product.detail?.colors?.FirstOrDefault();

        return new ProductToListDto
        {
            Name = product.name,
            Brand = product.brand?.brandGroupCode??"",
            Description = product.detail?.colors[0].description??"",
            Price = firstColor?.pricing?.price?.value ?? 0,
            DiscountedPrice = null,
            Colors = new List<ScrapperWebAPI.Models.ProductDtos.Color>
            {
                MapColor(root)
            },
            Sizes = MapSizes(root),
            ImageUrl = firstColor?.mainImgs?.Select(img => img.url).ToList() ?? new List<string>()
        };
    }

    public static List<Sizes> MapSizes(Root root)
    {
        var firstColor = root?.product?.detail?.colors?.FirstOrDefault();

        return firstColor?.sizes?.Select(s => new Sizes
        {
            SizeName = s.name,
            OnStock = s.availability == "in_stock"
        }).ToList() ?? new List<Sizes>();
    }

    public static ScrapperWebAPI.Models.ProductDtos.Color MapColor(Root root)
    {
        var firstColor = root?.product?.detail?.colors?.FirstOrDefault();

        return new ScrapperWebAPI.Models.ProductDtos.Color
        {
            Name = firstColor?.name,
            Hex = firstColor?.hexCode
        };
    }
}
